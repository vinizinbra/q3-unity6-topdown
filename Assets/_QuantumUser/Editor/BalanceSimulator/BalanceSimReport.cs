namespace QuantumUser.Editor.BalanceSimulator
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Text;

    // Fixed column layout shared by the prediction export, the in-match BalanceRunRecorder CSV and
    // the Predicted-vs-Actual view - keep Col in sync with BalanceRunRecorder's writer.
    public enum Col
    {
        Minute, SurvivalTime, Level, Xp, PartyKills, MyKills,
        KillsFiller, KillsNormal, KillsSpecialist, KillsHeavy, KillsElite,
        SpawnedMin, Alive, Budget,
        WeaponDps, SkillDps, TotalDps, ExpectedDps, DpsRatio,
        WeaponLevel, Perks, CoinsEarned, CoinsSpent, Coins,
        HpNormal, HpHeavy, HpElite, TtkNormal, TtkHeavy, TtkElite,
        KillsPerMin, SpawnsPerMin, PressureFill, IdlePct, BossHp, BossTtk,
        BreakCoins, BreakLoopCost, BreakAfford, LoopCostToDate, LoopAfford, OrbPickup,
    }

    public class MinuteRow
    {
        public static readonly int ColumnCount = Enum.GetValues(typeof(Col)).Length;
        public string Phase = "";
        public string Weapon = "";
        public double[] V = new double[ColumnCount];

        public double this[Col c] { get => V[(int)c]; set => V[(int)c] = value; }
        public int Minute => (int)Math.Round(this[Col.Minute]);
    }

    public class HeroResult
    {
        public int PlayerCount = 1;
        public string HeroName;
        public string SkillModel;
        public List<MinuteRow> Rows = new();
        public List<string> PickLog = new();
        public Dictionary<int, MinuteRow> Actual = new();
    }

    public static class BalanceSimReport
    {
        public static readonly string[] ColumnNames = Enum.GetNames(typeof(Col));

        public static readonly Col[] TableColumns =
        {
            Col.Minute, Col.SurvivalTime, Col.Level, Col.Xp, Col.PartyKills, Col.MyKills, Col.SpawnedMin, Col.Alive, Col.Budget,
            Col.WeaponDps, Col.SkillDps, Col.TotalDps, Col.ExpectedDps, Col.DpsRatio, Col.WeaponLevel, Col.Perks,
            Col.CoinsEarned, Col.CoinsSpent, Col.Coins, Col.HpNormal, Col.HpHeavy, Col.HpElite, Col.TtkNormal, Col.TtkHeavy, Col.TtkElite,
            Col.KillsPerMin, Col.SpawnsPerMin, Col.PressureFill, Col.IdlePct, Col.BossHp, Col.BossTtk,
            Col.BreakCoins, Col.BreakLoopCost, Col.BreakAfford, Col.LoopCostToDate, Col.LoopAfford, Col.OrbPickup,
        };

        // Columns a live recording can produce, and therefore the ones shown as Predicted/Actual pairs.
        public static readonly Col[] CompareColumns =
        {
            Col.Level, Col.PartyKills, Col.MyKills, Col.TotalDps, Col.WeaponDps, Col.WeaponLevel, Col.Perks, Col.CoinsEarned, Col.Coins, Col.Alive, Col.OrbPickup,
        };

        public static List<MinuteRow> Average(List<List<MinuteRow>> perSeed)
        {
            var result = new List<MinuteRow>();
            int minutes = perSeed.Max(rows => rows.Count);

            for (int m = 0; m < minutes; m++)
            {
                List<MinuteRow> samples = perSeed.Where(rows => m < rows.Count).Select(rows => rows[m]).ToList();
                var avg = new MinuteRow();

                for (int c = 0; c < MinuteRow.ColumnCount; c++)
                    avg.V[c] = samples.Average(r => r.V[c]);

                avg.Phase = MostCommon(samples.Select(r => r.Phase));
                avg.Weapon = MostCommon(samples.Select(r => r.Weapon));
                result.Add(avg);
            }

            return result;
        }

        private static string MostCommon(IEnumerable<string> values)
            => values.GroupBy(v => v).OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault() ?? "";

        public static string Format(Col col, double value) => col switch
        {
            Col.Minute or Col.Level or Col.WeaponLevel or Col.Perks or Col.Alive => value.ToString("0.#", CultureInfo.InvariantCulture),
            Col.DpsRatio or Col.PressureFill or Col.IdlePct or Col.BreakAfford or Col.LoopAfford or Col.OrbPickup => value.ToString("0.00", CultureInfo.InvariantCulture),
            Col.TtkNormal or Col.TtkHeavy or Col.TtkElite or Col.BossTtk => value.ToString("0.0", CultureInfo.InvariantCulture),
            _ => value.ToString("0", CultureInfo.InvariantCulture),
        };

        public static string ToCsv(List<HeroResult> results)
        {
            var sb = new StringBuilder();
            sb.Append("Hero,Phase,Weapon,Players,").AppendLine(string.Join(",", ColumnNames));

            foreach (HeroResult hero in results)
            {
                foreach (MinuteRow row in hero.Rows)
                {
                    sb.Append(Escape(hero.HeroName)).Append(',').Append(Escape(row.Phase)).Append(',').Append(Escape(row.Weapon)).Append(',').Append(hero.PlayerCount);
                    for (int c = 0; c < MinuteRow.ColumnCount; c++)
                        sb.Append(',').Append(row.V[c].ToString("0.###", CultureInfo.InvariantCulture));
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        public static string ToMarkdown(HeroResult hero)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"### {hero.HeroName} ({hero.PlayerCount}P) - skill model: {hero.SkillModel}");
            sb.Append("| Min | Phase | Weapon |");
            foreach (Col col in TableColumns.Skip(1))
                sb.Append(' ').Append(col).Append(" |");
            sb.AppendLine();
            sb.Append("|---|---|---|");
            foreach (Col _ in TableColumns.Skip(1))
                sb.Append("---|");
            sb.AppendLine();

            foreach (MinuteRow row in hero.Rows)
            {
                sb.Append("| ").Append(row.Minute).Append(" | ").Append(row.Phase).Append(" | ").Append(row.Weapon).Append(" |");
                foreach (Col col in TableColumns.Skip(1))
                    sb.Append(' ').Append(Format(col, row[col])).Append(" |");
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static string Escape(string value) => value != null && value.Contains(',') ? $"\"{value.Replace("\"", "\"\"")}\"" : value ?? "";

        // Reads a recorder CSV (same header layout as ToCsv) into per-hero, per-minute rows.
        public static Dictionary<string, Dictionary<int, MinuteRow>> LoadCsv(string path)
        {
            var result = new Dictionary<string, Dictionary<int, MinuteRow>>(StringComparer.OrdinalIgnoreCase);
            string[] lines = File.ReadAllLines(path);

            if (lines.Length < 2)
                return result;

            string[] header = SplitCsv(lines[0]);
            var columnIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
                columnIndex[header[i].Trim()] = i;

            for (int l = 1; l < lines.Length; l++)
            {
                string[] cells = SplitCsv(lines[l]);
                if (cells.Length < 3)
                    continue;

                string hero = cells[columnIndex.TryGetValue("Hero", out int h) ? h : 0];
                var row = new MinuteRow
                {
                    Phase = columnIndex.TryGetValue("Phase", out int p) && p < cells.Length ? cells[p] : "",
                    Weapon = columnIndex.TryGetValue("Weapon", out int w) && w < cells.Length ? cells[w] : "",
                };

                for (int c = 0; c < MinuteRow.ColumnCount; c++)
                {
                    if (columnIndex.TryGetValue(ColumnNames[c], out int idx) && idx < cells.Length
                        && double.TryParse(cells[idx], NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
                        row.V[c] = value;
                }

                if (result.TryGetValue(hero, out var rows) == false)
                    result[hero] = rows = new Dictionary<int, MinuteRow>();

                rows[row.Minute] = row;
            }

            return result;
        }

        private static string[] SplitCsv(string line)
        {
            var cells = new List<string>();
            var current = new StringBuilder();
            bool quoted = false;

            foreach (char ch in line)
            {
                if (ch == '"') { quoted = !quoted; continue; }
                if (ch == ',' && quoted == false) { cells.Add(current.ToString()); current.Clear(); continue; }
                current.Append(ch);
            }

            cells.Add(current.ToString());
            return cells.ToArray();
        }
    }
}
