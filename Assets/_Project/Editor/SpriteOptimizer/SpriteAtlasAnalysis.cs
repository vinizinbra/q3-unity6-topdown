using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace Project.EditorTools.SpriteOptimizer
{
    /// <summary>One "Type.field" key seen during the scan, for the rules table.</summary>
    internal sealed class RuleRow
    {
        public string Key;
        public SpriteContext Auto;
        public SpriteContext Current;
        public int Usages;
        public int Sprites;
        public UsageSource Source;
    }

    /// <summary>Per-texture row of the "sprites used in UI / Gameplay" lists.</summary>
    internal sealed class TextureRow
    {
        public TextureInfo Texture;
        public List<SpriteInfo> Sprites;
        public int UsageCount;
        public string Status;
        public Severity Severity;
    }

    /// <summary>
    /// Resolves each usage's context (field rules + texture overrides), reads atlas memberships fresh, and
    /// produces the issue list with fixes. Cheap — re-run after any atlas/settings change; only reference
    /// changes (duplicates) need a new <see cref="SpriteUsageScanner"/> scan.
    /// </summary>
    internal sealed class SpriteAtlasAnalysis
    {
        public ScanResult Scan;
        public SpriteOptimizerSettings Settings;
        public List<AtlasInfo> Atlases = new List<AtlasInfo>();
        public readonly List<Issue> Issues = new List<Issue>();
        public readonly List<RuleRow> Rules = new List<RuleRow>();
        public readonly Dictionary<SpriteContext, List<TextureRow>> Rows = new Dictionary<SpriteContext, List<TextureRow>>();

        public static SpriteAtlasAnalysis Run(ScanResult scan, SpriteOptimizerSettings settings)
        {
            var a = new SpriteAtlasAnalysis { Scan = scan, Settings = settings };
            a.Analyze();
            return a;
        }

        public IEnumerable<Issue> IssuesFor(SpriteContext tab) => Issues.Where(i => i.Context == tab);

        private void Analyze()
        {
            foreach (TextureInfo t in Scan.Textures.Values)
            {
                t.Memberships.Clear();
                foreach (SpriteInfo s in t.Sprites.Values)
                {
                    s.PackedIn.Clear();
                    s.UsedUI = s.UsedGameplay = s.HasUnknownUsage = false;
                }
            }

            Atlases = AtlasReader.ReadAll(Settings, Scan.GetOrAddTexture);

            ResolveContexts();
            AnalyzeAtlases();
            foreach (TextureInfo texture in Scan.Textures.Values.OrderBy(t => t.Path))
                AnalyzeTexture(texture);
            AnalyzeRules();
            BuildRows();

            Issues.Sort((x, y) =>
            {
                int c = y.Severity.CompareTo(x.Severity);
                if (c != 0) return c;
                c = x.Kind.CompareTo(y.Kind);
                return c != 0 ? c : string.CompareOrdinal(x.Texture?.Path ?? x.Atlas?.Path ?? x.RuleKey, y.Texture?.Path ?? y.Atlas?.Path ?? y.RuleKey);
            });
        }

        // ------------------------------------------------------------------ contexts

        private void ResolveContexts()
        {
            foreach (TextureInfo texture in Scan.Textures.Values)
            {
                texture.Override = Settings.OverrideFor(texture.Path);

                foreach (AtlasMembership m in texture.Memberships)
                    foreach (SpriteInfo s in texture.Sprites.Values)
                        if (m.Covers(s) && !s.PackedIn.Contains(m.Atlas))
                            s.PackedIn.Add(m.Atlas);

                foreach (SpriteInfo sprite in texture.Sprites.Values)
                {
                    foreach (SpriteUsage u in sprite.Usages)
                    {
                        SpriteContext rule = Settings.RuleFor(u.RuleKey);
                        u.Context = rule != SpriteContext.Unknown ? rule : u.AutoContext;
                        if (texture.Override == SpriteContext.UI || texture.Override == SpriteContext.Gameplay)
                            u.Context = texture.Override;

                        switch (u.Context)
                        {
                            case SpriteContext.UI: sprite.UsedUI = true; break;
                            case SpriteContext.Gameplay: sprite.UsedGameplay = true; break;
                            case SpriteContext.Unknown: sprite.HasUnknownUsage = true; break;
                        }
                    }

                    if (texture.Override == SpriteContext.Ignore)
                        sprite.UsedUI = sprite.UsedGameplay = false;
                }
            }
        }

        // ------------------------------------------------------------------ atlases

        private void AnalyzeAtlases()
        {
            foreach (AtlasInfo atlas in Atlases)
            {
                if (!atlas.Bound)
                {
                    var issue = NewIssue(IssueKind.AtlasUnbound, Severity.Warning, SpriteContext.Unknown, atlas: atlas,
                        message: $"{atlas.Name} has no context; the optimizer leaves its packables alone. Bind it in Settings.");
                    foreach (SpriteContext c in new[] { SpriteContext.UI, SpriteContext.Gameplay, SpriteContext.Ignore })
                    {
                        SpriteContext ctx = c;
                        issue.Fixes.Add(new IssueFix
                        {
                            Label = $"Bind {SpriteOptimizerUtil.ContextLabel(ctx)}",
                            Run = () => Bind(atlas.Atlas, ctx),
                        });
                    }
                }

                if (atlas.RawEntries > atlas.DistinctEntries + atlas.NullEntries)
                {
                    NewIssue(IssueKind.DuplicateEntries, Severity.Warning, TabFor(atlas), atlas: atlas,
                            message: $"{atlas.Name} lists {atlas.RawEntries - atlas.NullEntries} packables but only {atlas.DistinctEntries} are distinct.")
                        .Fixes.Add(new IssueFix { Label = "Remove duplicates", Plan = b => b.Dedupe(atlas.Atlas), Safe = true });
                }

                if (atlas.NullEntries > 0)
                {
                    NewIssue(IssueKind.NullEntries, Severity.Info, TabFor(atlas), atlas: atlas,
                            message: $"{atlas.Name} has {atlas.NullEntries} empty / missing packable entries.")
                        .Fixes.Add(new IssueFix { Label = "Remove missing", Plan = b => b.DropNulls(atlas.Atlas), Safe = true });
                }
            }

            foreach (SpriteContext c in new[] { SpriteContext.UI, SpriteContext.Gameplay })
            {
                if (Settings.PrimaryFor(c) == null)
                    NewIssue(IssueKind.NoPrimaryAtlas, Severity.Error, c,
                        message: $"No atlas is bound to {c}. Create one and bind it in Settings — nothing can be packed for {c} until then.");
            }
        }

        private static SpriteContext TabFor(AtlasInfo atlas) => atlas.Managed ? atlas.Context : SpriteContext.Unknown;

        // ------------------------------------------------------------------ textures

        private void AnalyzeTexture(TextureInfo tex)
        {
            List<AtlasInfo> atlases = tex.Atlases.ToList();

            // Sprite area estimate for page overflow, counted once per atlas it's packed in.
            foreach (SpriteInfo s in tex.Sprites.Values)
                foreach (AtlasInfo a in s.PackedIn)
                {
                    a.PackedArea += (long)(s.Rect.width + a.Padding * 2) * (long)(s.Rect.height + a.Padding * 2);
                    a.SpriteCount++;
                }

            if (!tex.IsSpriteType)
            {
                if (atlases.Count > 0)
                    AddRemoveEverywhere(IssueKind.NonSpriteInAtlas, Severity.Warning, tex, atlases,
                        "Texture Type isn't Sprite, so the atlas can't pack it. Remove it or change the import type.", safe: true);
                return;
            }

            if (atlases.Count > 0)
            {
                foreach (AtlasMembership folder in tex.Memberships.Where(m => m.Kind == PackKind.Folder))
                    NewIssue(IssueKind.FolderPackable, Severity.Info, TabFor(folder.Atlas), tex, atlas: folder.Atlas,
                        message: $"Packed through folder '{folder.FolderPath}' in {folder.Atlas.Name}; the optimizer can't remove single textures from a folder packable.");
            }

            if (tex.Override == SpriteContext.Ignore)
            {
                CheckMultipleAtlases(tex);
                return;
            }

            List<SpriteInfo> used = tex.Sprites.Values.Where(s => s.UsedUI || s.UsedGameplay).ToList();
            List<SpriteInfo> cross = used.Where(s => s.CrossContext).ToList();
            List<SpriteInfo> uiOnly = used.Where(s => s.UsedUI && !s.UsedGameplay).ToList();
            List<SpriteInfo> gpOnly = used.Where(s => s.UsedGameplay && !s.UsedUI).ToList();

            CheckMultipleAtlases(tex);

            foreach (SpriteInfo s in cross)
                AddCrossContext(tex, s);

            if (used.Count == 0 && !tex.Sprites.Values.Any(s => s.HasUnknownUsage))
            {
                // Textures in Ignore / unbound atlases are someone else's decision.
                List<AtlasInfo> managed = atlases.Where(a => a.Managed).ToList();
                if (managed.Count > 0) AddUnused(tex, managed);
                return;
            }

            if (tex.IsTmpSpriteSheet && atlases.Count > 0)
                AddRemoveEverywhere(IssueKind.TmpSheetInAtlas, Severity.Warning, tex, atlases,
                    "TextMesh Pro draws sprite-asset glyphs straight from this texture with its own material; the atlas copy is never used" +
                    (used.Count > 0 ? " by TMP (some sprites are also used elsewhere)." : "."), safe: used.Count == 0);

            if (tex.RawUsers.Count > 0 && atlases.Count > 0 && used.Count > 0 && !tex.IsTmpSpriteSheet)
                NewIssue(IssueKind.RawTextureInAtlas, Severity.Info, TabFor(atlases[0]), tex,
                    message: $"Also referenced directly as a texture by {tex.RawUsers.Count} material(s)/RawImage(s) (e.g. {tex.RawUsers[0]}), so it ships twice: once in the atlas, once raw.");

            // Anything already packed in an Ignore / unbound atlas is someone else's decision.
            if (atlases.Any(a => !a.Managed)) return;

            if (uiOnly.Count > 0 && gpOnly.Count > 0)
            {
                AddSplit(tex, uiOnly, gpOnly, cross);
                return;
            }

            if (uiOnly.Count > 0) PlaceSingleContext(tex, SpriteContext.UI, uiOnly, cross);
            else if (gpOnly.Count > 0) PlaceSingleContext(tex, SpriteContext.Gameplay, gpOnly, cross);
        }

        private void PlaceSingleContext(TextureInfo tex, SpriteContext context, List<SpriteInfo> sprites, List<SpriteInfo> cross)
        {
            SpriteContext other = context == SpriteContext.UI ? SpriteContext.Gameplay : SpriteContext.UI;
            List<AtlasInfo> own = tex.Atlases.Where(a => a.Context == context).ToList();
            List<AtlasInfo> wrong = tex.Atlases.Where(a => a.Context == other).ToList();
            SpriteAtlas target = own.FirstOrDefault()?.Atlas ?? Settings.PrimaryFor(context);

            List<SpriteInfo> missing = sprites.Where(s => s.PackedIn.All(a => a.Context != context)).ToList();
            bool crossBlocks = cross.Count > 0;

            if (wrong.Count > 0)
            {
                var issue = NewIssue(IssueKind.WrongContextAtlas, Severity.Error, context, tex,
                    message: $"Used only by {context} ({Usages(sprites)}) but packed in {Names(wrong)}. " +
                             $"{context} draws then pull in {other} atlas pages and break batches." +
                             (crossBlocks ? " Resolve its UI + Gameplay sprites (Conflicts tab) first." : ""));
                if (!crossBlocks && target != null)
                {
                    issue.Fixes.Add(new IssueFix
                    {
                        Label = $"Move → {target.name}",
                        Safe = true,
                        Plan = b =>
                        {
                            foreach (AtlasInfo w in wrong) b.RemoveTexture(w.Atlas, tex.Path);
                            if (missing.Count > 0 || own.Count == 0) b.AddTexture(target, tex.Path);
                        },
                    });
                }
                return;
            }

            if (missing.Count > 0)
            {
                var issue = NewIssue(IssueKind.MissingFromAtlas, Severity.Warning, context, tex,
                    message: $"{missing.Count}/{tex.Sprites.Count} sprite(s) used by {context} are in no {context} atlas " +
                             $"({Usages(missing)}); each draws from its own texture and breaks the batch.");
                if (target != null)
                {
                    issue.Fixes.Add(new IssueFix
                    {
                        Label = $"Add → {target.name}",
                        Safe = true,
                        Plan = b =>
                        {
                            // A sheet already packed per sprite stays per sprite; otherwise pack the whole texture.
                            if (tex.Memberships.Any(m => m.Kind == PackKind.Sprite && m.Atlas.Atlas == target))
                                b.AddSprites(target, missing.Select(s => s.Load()));
                            else
                                b.AddTexture(target, tex.Path);
                        },
                    });
                }
            }
        }

        private void AddSplit(TextureInfo tex, List<SpriteInfo> uiOnly, List<SpriteInfo> gpOnly, List<SpriteInfo> cross)
        {
            SpriteAtlas uiAtlas = tex.Atlases.FirstOrDefault(a => a.Context == SpriteContext.UI)?.Atlas ?? Settings.PrimaryFor(SpriteContext.UI);
            SpriteAtlas gpAtlas = tex.Atlases.FirstOrDefault(a => a.Context == SpriteContext.Gameplay)?.Atlas ?? Settings.PrimaryFor(SpriteContext.Gameplay);

            bool alreadyOk =
                uiOnly.All(s => s.PackedIn.Count == 1 && s.PackedIn[0].Context == SpriteContext.UI) &&
                gpOnly.All(s => s.PackedIn.Count == 1 && s.PackedIn[0].Context == SpriteContext.Gameplay) &&
                tex.Memberships.All(m => m.Kind == PackKind.Sprite);
            if (alreadyOk) return;

            var issue = NewIssue(IssueKind.SplitSheet, Severity.Warning, SpriteContext.Unknown, tex,
                message: $"Sheet has {uiOnly.Count} UI-only and {gpOnly.Count} Gameplay-only sprite(s). Packing the whole texture puts " +
                         "the other context's sprites in the wrong atlas; pack each sprite into its own context's atlas instead." +
                         (cross.Count > 0 ? $" {cross.Count} sprite(s) are used by both — duplicate them first." : ""));

            if (cross.Count == 0 && uiAtlas != null && gpAtlas != null)
            {
                issue.Fixes.Add(new IssueFix
                {
                    Label = "Pack per sprite",
                    Safe = true,
                    Plan = b =>
                    {
                        foreach (AtlasInfo a in tex.Atlases.Where(a => a.Managed)) b.RemoveTexture(a.Atlas, tex.Path);
                        b.AddSprites(uiAtlas, uiOnly.Select(s => s.Load()));
                        b.AddSprites(gpAtlas, gpOnly.Select(s => s.Load()));
                    },
                });
            }
        }

        private static SpriteContext PackedContext(SpriteInfo sprite) =>
            sprite.PackedIn.Select(a => a.Context).FirstOrDefault(c => c == SpriteContext.UI || c == SpriteContext.Gameplay);

        /// <summary>
        /// The side that gets the copy: the one that does NOT already own the atlas copy of the original.
        /// Unpacked originals are copied for UI, so gameplay code/data that loads the original keeps working.
        /// </summary>
        public static SpriteContext DefaultDuplicateTarget(SpriteInfo sprite) =>
            PackedContext(sprite) == SpriteContext.UI ? SpriteContext.Gameplay : SpriteContext.UI;

        private void AddCrossContext(TextureInfo tex, SpriteInfo sprite)
        {
            SpriteContext packedContext = PackedContext(sprite);
            SpriteContext defaultTarget = DefaultDuplicateTarget(sprite);

            List<SpriteUsage> ui = sprite.Usages.Where(u => u.Context == SpriteContext.UI).ToList();
            List<SpriteUsage> gp = sprite.Usages.Where(u => u.Context == SpriteContext.Gameplay).ToList();

            var issue = NewIssue(IssueKind.CrossContextSprite, Severity.Error, SpriteContext.Unknown, tex, sprite,
                message: $"'{sprite.Name}' is used by UI ({ui.Count}) and Gameplay ({gp.Count}). A sprite can only live in one atlas, " +
                         "so one side always draws from the other side's atlas page. Duplicate it for one side and repoint that side's references." +
                         (packedContext != SpriteContext.Unknown ? $" Currently packed for {packedContext}." : ""));

            foreach (SpriteContext target in new[] { defaultTarget, defaultTarget == SpriteContext.UI ? SpriteContext.Gameplay : SpriteContext.UI })
            {
                SpriteContext t = target;
                List<SpriteUsage> moving = t == SpriteContext.UI ? ui : gp;
                int fixedCount = moving.Count(u => u.CanReassign);
                issue.Fixes.Add(new IssueFix
                {
                    Label = $"Duplicate → {t}" + (t == defaultTarget ? " ★" : ""),
                    Tooltip = $"Creates a {t} copy of '{sprite.Name}', packs it in the {t} atlas and repoints {fixedCount} {t} reference(s)." +
                              (moving.Count > fixedCount ? $" {moving.Count - fixedCount} runtime/animation reference(s) must be changed by hand." : ""),
                    NeedsRescan = true,
                    Run = () => SpriteDuplicator.DuplicateAndReassign(new[] { sprite }, t, Settings),
                });
            }
        }

        private void AddUnused(TextureInfo tex, List<AtlasInfo> atlases)
        {
            string why = tex.RawUsers.Count > 0
                ? $"Only referenced as a raw texture ({tex.RawUsers.Count}: e.g. {tex.RawUsers[0]}) — materials/RawImages never sample the atlas, so the packed copy is pure waste."
                : "No scanned prefab, scene, data asset, clip or runtime recording uses any of its sprites.";
            if (tex.InResources)
                why += " It is under a Resources folder and may be loaded by path — check before removing.";

            Severity severity = tex.RawUsers.Count > 0 || tex.IsTmpSpriteSheet ? Severity.Warning : Severity.Info;
            IssueKind kind = tex.IsTmpSpriteSheet ? IssueKind.TmpSheetInAtlas : IssueKind.UnusedInAtlas;
            AddRemoveEverywhere(kind, severity, tex, atlases, why, safe: false);
        }

        private void AddRemoveEverywhere(IssueKind kind, Severity severity, TextureInfo tex, List<AtlasInfo> atlases, string message, bool safe)
        {
            foreach (AtlasInfo atlas in atlases)
            {
                if (tex.Memberships.All(m => m.Atlas != atlas || m.Kind == PackKind.Folder)) continue;
                NewIssue(kind, severity, TabFor(atlas), tex, atlas: atlas, message: $"[{atlas.Name}] {message}")
                    .Fixes.Add(new IssueFix
                    {
                        Label = $"Remove from {atlas.Name}",
                        Safe = safe && atlas.Managed,
                        Plan = b => b.RemoveTexture(atlas.Atlas, tex.Path),
                    });
            }
        }

        private void CheckMultipleAtlases(TextureInfo tex)
        {
            List<SpriteInfo> multi = tex.Sprites.Values.Where(s => s.PackedIn.Count > 1).ToList();
            if (multi.Count == 0) return;

            List<AtlasInfo> atlases = multi.SelectMany(s => s.PackedIn).Distinct().ToList();
            SpriteContext needed = multi.Any(s => s.UsedUI) && !multi.Any(s => s.UsedGameplay) ? SpriteContext.UI
                : multi.Any(s => s.UsedGameplay) && !multi.Any(s => s.UsedUI) ? SpriteContext.Gameplay
                : SpriteContext.Unknown;

            var issue = NewIssue(IssueKind.MultipleAtlases, Severity.Error, SpriteContext.Unknown, tex,
                message: $"{multi.Count} sprite(s) packed in {Names(atlases)}. Unity binds each sprite to one atlas at load " +
                         "(and warns about it), so the other copy is wasted memory and the choice of page is arbitrary — batches break." +
                         (needed != SpriteContext.Unknown ? $" Used only by {needed}." : ""));

            foreach (AtlasInfo keep in atlases.OrderByDescending(a => a.Context == needed))
            {
                AtlasInfo k = keep;
                bool canRemoveOthers = atlases.Where(a => a != k)
                    .All(a => tex.Memberships.Any(m => m.Atlas == a && m.Kind != PackKind.Folder));
                if (!canRemoveOthers) continue;
                issue.Fixes.Add(new IssueFix
                {
                    Label = $"Keep in {k.Name}" + (k.Context == needed ? " ★" : ""),
                    Safe = k.Context == needed && k.Managed,
                    Plan = b =>
                    {
                        foreach (AtlasInfo other in atlases.Where(a => a != k)) b.RemoveTexture(other.Atlas, tex.Path);
                    },
                });
            }
        }

        // ------------------------------------------------------------------ rules

        private void AnalyzeRules()
        {
            foreach (IGrouping<string, SpriteUsage> group in Scan.Usages.Where(u => u.RuleKey != null).GroupBy(u => u.RuleKey))
            {
                SpriteUsage first = group.First();
                var row = new RuleRow
                {
                    Key = group.Key,
                    Auto = first.AutoContext,
                    Current = Settings.RuleFor(group.Key),
                    Usages = group.Count(),
                    Sprites = group.Select(u => u.Sprite).Distinct().Count(),
                    Source = first.Source,
                };
                Rules.Add(row);

                if (row.Current != SpriteContext.Unknown || row.Auto != SpriteContext.Unknown) continue;

                var issue = NewIssue(IssueKind.UnresolvedRule, Severity.Warning, SpriteContext.Unknown,
                    message: $"'{group.Key}' holds {row.Sprites} sprite(s) across {row.Usages} reference(s) and the scanner can't tell " +
                             "whether they end up in UI or in the world. Those sprites are left out of placement until you decide " +
                             "(or record a play session).");
                issue.RuleKey = group.Key;
                foreach (SpriteContext c in new[] { SpriteContext.UI, SpriteContext.Gameplay, SpriteContext.Ignore })
                {
                    SpriteContext ctx = c;
                    issue.Fixes.Add(new IssueFix { Label = SpriteOptimizerUtil.ContextLabel(ctx), Run = () => Settings.SetRule(group.Key, ctx) });
                }
            }
            Rules.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));
        }

        // ------------------------------------------------------------------ rows

        private void BuildRows()
        {
            foreach (AtlasInfo atlas in Atlases.Where(a => a.Managed && a.EstimatedPages > 1))
            {
                NewIssue(IssueKind.AtlasOverflow, Severity.Warning, atlas.Context, atlas: atlas,
                    message: $"{atlas.Name}: ~{atlas.SpriteCount} sprites need about {atlas.EstimatedPages} pages of {atlas.MaxSize}². " +
                             "Sprites on different pages are different textures and never batch together. Raise Max Texture Size, " +
                             "remove unused textures, or split the atlas by screen. (Estimate — use Pack Preview for the real count.)");
            }

            foreach (SpriteContext c in new[] { SpriteContext.UI, SpriteContext.Gameplay })
            {
                var rows = new List<TextureRow>();
                foreach (TextureInfo tex in Scan.Textures.Values)
                {
                    List<SpriteInfo> sprites = tex.Sprites.Values.Where(s => s.UsedIn(c)).OrderBy(s => s.Name).ToList();
                    if (sprites.Count == 0) continue;

                    int packedRight = sprites.Count(s => s.PackedIn.Any(a => a.Context == c || !a.Managed));
                    List<AtlasInfo> wrong = sprites.SelectMany(s => s.PackedIn).Where(a => a.Managed && a.Context != c).Distinct().ToList();
                    int cross = sprites.Count(s => s.CrossContext);

                    string status;
                    Severity sev;
                    if (cross > 0) { status = $"{cross} shared with {(c == SpriteContext.UI ? "Gameplay" : "UI")}"; sev = Severity.Error; }
                    else if (wrong.Count > 0) { status = $"in {Names(wrong)}"; sev = Severity.Error; }
                    else if (packedRight == sprites.Count) { status = string.Join(", ", sprites.SelectMany(s => s.PackedIn).Distinct().Select(a => a.Name)); sev = Severity.Info; }
                    else if (packedRight == 0) { status = "not packed"; sev = Severity.Warning; }
                    else { status = $"{packedRight}/{sprites.Count} packed"; sev = Severity.Warning; }

                    rows.Add(new TextureRow
                    {
                        Texture = tex,
                        Sprites = sprites,
                        UsageCount = sprites.Sum(s => s.Usages.Count(u => u.Context == c)),
                        Status = status,
                        Severity = sev,
                    });
                }
                rows.Sort((x, y) =>
                {
                    int s = y.Severity.CompareTo(x.Severity);
                    return s != 0 ? s : string.CompareOrdinal(x.Texture.Path, y.Texture.Path);
                });
                Rows[c] = rows;
            }
        }

        // ------------------------------------------------------------------ helpers

        private Issue NewIssue(IssueKind kind, Severity severity, SpriteContext tab, TextureInfo texture = null,
            SpriteInfo sprite = null, AtlasInfo atlas = null, string message = null)
        {
            var issue = new Issue
            {
                Kind = kind,
                Severity = severity,
                Context = tab,
                Texture = texture,
                Sprite = sprite,
                Atlas = atlas,
                Message = message,
            };
            Issues.Add(issue);
            return issue;
        }

        private void Bind(SpriteAtlas atlas, SpriteContext context)
        {
            AtlasBinding binding = Settings.BindingFor(atlas);
            if (binding == null) Settings.Atlases.Add(binding = new AtlasBinding { Atlas = atlas });
            binding.Context = context;
            if (Settings.PrimaryFor(context) == atlas || Settings.Atlases.All(b => b.Context != context || !b.Primary || b == binding))
                Settings.SetPrimary(binding);
            Settings.MarkDirty();
        }

        private static string Names(IEnumerable<AtlasInfo> atlases) => string.Join(" + ", atlases.Select(a => a.Name));

        private static string Usages(IEnumerable<SpriteInfo> sprites)
        {
            List<SpriteUsage> all = sprites.SelectMany(s => s.Usages).ToList();
            SpriteUsage first = all.FirstOrDefault();
            return first == null ? "no references" : all.Count == 1 ? first.Describe() : $"{all.Count} refs, e.g. {first.Describe()}";
        }
    }
}
