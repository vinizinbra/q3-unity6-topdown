namespace Quantum.Editor
{
    using System;
    using System.Collections;
    using System.Collections.Generic;
    using System.Linq;
    using System.Reflection;
    using System.Text;
    using QuantumUser.View.Util;
    using UnityEditor;
    using UnityEngine;

    // Neither the Quantum SDK nor this project has any tool that checks AssetRef<T> field content -
    // QuantumUnityDBImporter only validates a registered asset's own identity (name/path/type), never
    // the fields elsewhere that point at it. This walks every AssetRef<T>/List<AssetRef<T>> field on
    // every AssetObject (via reflection, not limited to [ExpandableAsset] fields) and reports fields
    // that are unassigned or point at a guid that doesn't resolve to any asset.
    public static class QuantumAssetValidator
    {
        private struct Issue
        {
            public string AssetPath;
            public string AssetName;
            public string FieldPath;
            public string Message;
            public bool IsError;
        }

        [MenuItem("Tools/Quantum/Validate Assets")]
        private static void ValidateAssets()
        {
            List<Issue> issues = new List<Issue>();

            foreach (AssetObject asset in EnumerateAllQuantumAssets())
                CollectIssues(asset, issues);

            int errorCount = issues.Count(issue => issue.IsError);
            int warningCount = issues.Count - errorCount;

            if (issues.Count == 0)
            {
                LogHelper.Log("QuantumAssetValidator", "No issues found - every AssetRef field is either assigned or resolves correctly.");
                return;
            }

            StringBuilder message = new StringBuilder();
            message.AppendLine($"[QuantumAssetValidator] {errorCount} dangling reference(s), {warningCount} unassigned field(s):");

            foreach (Issue issue in issues.OrderByDescending(issue => issue.IsError))
                message.AppendLine($"{(issue.IsError ? "ERROR" : "warn ")}  {issue.AssetName} ({issue.AssetPath}) . {issue.FieldPath}: {issue.Message}");

            if (errorCount > 0)
                LogHelper.Error("QuantumAssetValidator", message.ToString());
            else
                LogHelper.Warn("QuantumAssetValidator", message.ToString());
        }

        private static IEnumerable<AssetObject> EnumerateAllQuantumAssets()
        {
            HashSet<string> paths = new HashSet<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:" + typeof(AssetObject).FullName))
                paths.Add(AssetDatabase.GUIDToAssetPath(guid));

            foreach (string path in paths)
            {
                foreach (UnityEngine.Object obj in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (obj is AssetObject assetObject)
                        yield return assetObject;
                }
            }
        }

        private static void CollectIssues(AssetObject asset, List<Issue> issues)
        {
            string assetPath = AssetDatabase.GetAssetPath(asset);

            foreach (FieldInfo field in GetAssetRefFields(asset.GetType()))
            {
                object rawValue = field.GetValue(asset);
                if (rawValue == null)
                    continue;

                if (rawValue is IEnumerable enumerable && rawValue is string == false)
                {
                    int index = 0;
                    foreach (object element in enumerable)
                    {
                        if (element != null)
                            CheckOneRef(asset, assetPath, $"{field.Name}[{index}]", element, issues);
                        index++;
                    }
                }
                else
                {
                    CheckOneRef(asset, assetPath, field.Name, rawValue, issues);
                }
            }
        }

        private static void CheckOneRef(AssetObject asset, string assetPath, string fieldLabel, object assetRefBoxed, List<Issue> issues)
        {
            AssetGuid guid = GetGuid(assetRefBoxed);

            if (guid.IsValid == false)
            {
                issues.Add(new Issue
                {
                    AssetPath = assetPath,
                    AssetName = asset.name,
                    FieldPath = fieldLabel,
                    Message = "unassigned",
                    IsError = false,
                });
                return;
            }

            // Dynamic guids are assigned at runtime (e.g. procedurally spawned assets) and have
            // nothing to resolve against in the editor - not a static-setup problem.
            if (guid.IsDynamic)
                return;

            if (ResolveAsset(guid) == null)
            {
                issues.Add(new Issue
                {
                    AssetPath = assetPath,
                    AssetName = asset.name,
                    FieldPath = fieldLabel,
                    Message = $"dangling reference, guid {guid.Value} does not resolve to any asset",
                    IsError = true,
                });
            }
        }

        private static AssetGuid GetGuid(object assetRefBoxed)
        {
            object idBoxed = assetRefBoxed.GetType().GetField("Id", BindingFlags.Instance | BindingFlags.Public).GetValue(assetRefBoxed);
            long value = (long)idBoxed.GetType().GetField("Value", BindingFlags.Instance | BindingFlags.Public).GetValue(idBoxed);
            return (AssetGuid)value;
        }

        private static AssetObject ResolveAsset(AssetGuid guid)
        {
            try
            {
                return QuantumUnityDB.GetGlobalAssetEditorInstance(guid);
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static IEnumerable<FieldInfo> GetAssetRefFields(Type type)
        {
            List<FieldInfo> fields = new List<FieldInfo>();
            for (Type current = type; current != null && current != typeof(object); current = current.BaseType)
            {
                fields.AddRange(current
                    .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(field => IsAssetRefFieldType(field.FieldType)));
            }

            return fields;
        }

        private static bool IsAssetRefFieldType(Type fieldType)
        {
            if (IsAssetRefType(fieldType))
                return true;

            if (fieldType.IsArray)
                return IsAssetRefType(fieldType.GetElementType());

            if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof(List<>))
                return IsAssetRefType(fieldType.GetGenericArguments()[0]);

            return false;
        }

        private static bool IsAssetRefType(Type type)
        {
            if (type == typeof(AssetRef))
                return true;

            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(AssetRef<>);
        }
    }
}
