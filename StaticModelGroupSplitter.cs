using Frosty.Core;
using Frosty.Core.Windows;
using FrostySdk;
using FrostySdk.Ebx;
using FrostySdk.IO;
using FrostySdk.Managers;
using SharpDX;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace NoHavokSplitPlugin
{
    public static class StaticModelGroupSplitter
    {
        public class SplitResult
        {
            public int SubWorldsScanned;
            public int SubWorldsModified;
            public int GroupsRemoved;
            public int WorldPartsTouched;
            public int InstancesCreated;
            public int MembersSkipped;
            public string WarningText = "";
        }

        class OpenWorldPart
        {
            public EbxAssetEntry Entry;
            public EbxAsset Asset;
            public dynamic Root;
            public bool Dirty;
        }

        public static SplitResult SplitAllSubWorlds(FrostyTaskWindow task)
        {
            List<EbxAssetEntry> subWorlds = new List<EbxAssetEntry>();
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("SubWorldData"))
                subWorlds.Add(entry);
            return SplitSubWorlds(subWorlds, task);
        }

        public static SplitResult SplitSubWorlds(IList<EbxAssetEntry> subWorlds, FrostyTaskWindow task)
        {
            SplitResult result = new SplitResult();
            List<string> warnings = new List<string>();
            if (subWorlds == null || subWorlds.Count == 0)
                return result;

            task?.Update("Building ObjectVariation map...");
            Dictionary<uint, EbxImportReference> variationMap = BuildVariationMap();

            HashSet<string> prefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (EbxAssetEntry entry in subWorlds)
            {
                string key = MapChoice.GetMapKey(entry.Name);
                if (!string.IsNullOrEmpty(key))
                    prefixes.Add(key + "/");
            }

            task?.Update("Collecting WorldPartData...");
            List<EbxAssetEntry> allWorldParts = new List<EbxAssetEntry>();
            foreach (EbxAssetEntry part in App.AssetManager.EnumerateEbx("WorldPartData"))
            {
                foreach (string prefix in prefixes)
                {
                    if (part.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        allWorldParts.Add(part);
                        break;
                    }
                }
            }

            for (int i = 0; i < subWorlds.Count; i++)
            {
                EbxAssetEntry entry = subWorlds[i];
                task?.Update($"Splitting {entry.Name}", (i / (double)Math.Max(1, subWorlds.Count)) * 100.0);

                try
                {
                    if (SplitSubWorld(entry, allWorldParts, variationMap, result, warnings))
                        result.SubWorldsModified++;
                }
                catch (Exception ex)
                {
                    warnings.Add($"{entry.Name}: {ex.Message}");
                    App.Logger.Log($"NoHavokSplit failed on {entry.Name}: {ex}");
                }

                result.SubWorldsScanned++;
            }

            if (warnings.Count > 0)
                result.WarningText = "\n\n" + string.Join("\n", warnings.GetRange(0, Math.Min(12, warnings.Count)));

            return result;
        }

        static Dictionary<uint, EbxImportReference> BuildVariationMap()
        {
            Dictionary<uint, EbxImportReference> map = new Dictionary<uint, EbxImportReference>();
            foreach (EbxAssetEntry entry in App.AssetManager.EnumerateEbx("ObjectVariation"))
            {
                try
                {
                    EbxAsset asset = App.AssetManager.GetEbx(entry);
                    dynamic root = asset.RootObject;
                    uint hash = (uint)root.NameHash;
                    if (hash != 0 && !map.ContainsKey(hash))
                    {
                        map[hash] = new EbxImportReference
                        {
                            FileGuid = entry.Guid,
                            ClassGuid = asset.RootInstanceGuid
                        };
                    }
                }
                catch
                {
                }
            }
            return map;
        }

        static bool SplitSubWorld(
            EbxAssetEntry entry,
            List<EbxAssetEntry> allWorldParts,
            Dictionary<uint, EbxImportReference> variationMap,
            SplitResult result,
            List<string> warnings)
        {
            EbxAsset asset = App.AssetManager.GetEbx(entry);
            dynamic subWorld = asset.RootObject;
            if (!TypeLibrary.IsSubClassOf(subWorld, "SubWorldData"))
                return false;

            IList objects = subWorld.Objects;
            List<PointerRef> groups = new List<PointerRef>();
            foreach (PointerRef pr in objects)
            {
                object resolved = ResolvePointer(pr, asset);
                if (resolved != null && TypeLibrary.IsSubClassOf(resolved, "StaticModelGroupEntityData"))
                    groups.Add(pr);
            }

            if (groups.Count == 0)
                return false;

            List<EbxAssetEntry> folderParts = CollectExistingWorldParts(entry, asset, objects, allWorldParts);
            if (folderParts.Count == 0)
            {
                warnings.Add($"{entry.Name}: no existing WorldPartData under this SubWorld, groups left untouched");
                return false;
            }

            Dictionary<Guid, OpenWorldPart> openParts = new Dictionary<Guid, OpenWorldPart>();
            HashSet<object> removed = new HashSet<object>();
            int groupsRemoved = 0;
            bool modified = false;

            foreach (PointerRef groupRef in groups)
            {
                dynamic group = ResolvePointer(groupRef, asset);
                if (group == null)
                    continue;

                int havokCursor = 0;
                List<Matrix> havokTransforms = LoadHavokTransformsForGroup(group, warnings, entry.Name);
                bool groupOk = true;

                IList memberDatas = group.MemberDatas;
                if (memberDatas != null)
                {
                    foreach (dynamic member in memberDatas)
                    {
                        if (!SplitMember(asset, group, member, folderParts, openParts, havokTransforms, ref havokCursor, variationMap, result, warnings, entry.Name))
                        {
                            result.MembersSkipped++;
                            groupOk = false;
                        }
                    }
                }

                if (!groupOk)
                {
                    warnings.Add($"{entry.Name}: group kept because some members could not be placed into an existing WorldPart");
                    continue;
                }

                CollectGroupInternals(group, removed);
                removed.Add(group);
                groupsRemoved++;
            }

            if (removed.Count > 0)
            {
                StripConnections(subWorld, removed);

                for (int i = objects.Count - 1; i >= 0; i--)
                {
                    PointerRef pr = (PointerRef)objects[i];
                    object resolved = ResolvePointer(pr, asset);
                    if (resolved != null && removed.Contains(resolved))
                        objects.RemoveAt(i);
                }

                foreach (object obj in removed)
                {
                    try { asset.RemoveObject(obj); }
                    catch { }
                }

                result.GroupsRemoved += groupsRemoved;
                asset.Update();
                App.AssetManager.ModifyEbx(entry.Name, asset);
                modified = true;
            }

            foreach (OpenWorldPart part in openParts.Values)
            {
                if (!part.Dirty)
                    continue;
                part.Asset.Update();
                App.AssetManager.ModifyEbx(part.Entry.Name, part.Asset);
                result.WorldPartsTouched++;
                modified = true;
            }

            return modified;
        }

        static List<EbxAssetEntry> CollectExistingWorldParts(
            EbxAssetEntry subWorldEntry,
            EbxAsset subWorldAsset,
            IList objects,
            List<EbxAssetEntry> allWorldParts)
        {
            Dictionary<Guid, EbxAssetEntry> parts = new Dictionary<Guid, EbxAssetEntry>();
            string prefix = subWorldEntry.Name.TrimEnd('/') + "/";

            foreach (EbxAssetEntry part in allWorldParts)
            {
                if (part.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    parts[part.Guid] = part;
            }

            foreach (PointerRef pr in objects)
            {
                object resolved = ResolvePointer(pr, subWorldAsset);
                if (resolved == null || !TypeLibrary.IsSubClassOf(resolved, "WorldPartReferenceObjectData"))
                    continue;

                PointerRef blueprint = (PointerRef)((dynamic)resolved).Blueprint;
                if (blueprint.Type != PointerRefType.External)
                    continue;

                EbxAssetEntry partEntry = App.AssetManager.GetEbxEntry(blueprint.External.FileGuid);
                if (partEntry != null && TypeLibrary.IsSubClassOf(partEntry.Type, "WorldPartData"))
                    parts[partEntry.Guid] = partEntry;
            }

            return new List<EbxAssetEntry>(parts.Values);
        }

        static bool SplitMember(
            EbxAsset subWorldAsset,
            dynamic group,
            dynamic member,
            List<EbxAssetEntry> folderParts,
            Dictionary<Guid, OpenWorldPart> openParts,
            List<Matrix> havokTransforms,
            ref int havokCursor,
            Dictionary<uint, EbxImportReference> variationMap,
            SplitResult result,
            List<string> warnings,
            string subWorldName)
        {
            uint instanceCount = (uint)member.InstanceCount;
            if (instanceCount == 0)
                return false;

            PointerRef memberType = (PointerRef)member.MemberType;
            PointerRef blueprintRef = ResolveBlueprintRef(memberType, null);
            if (blueprintRef.Type == PointerRefType.Null)
            {
                warnings.Add($"{subWorldName}: MemberType missing, skipped {instanceCount} instances");
                ConsumeHavok(member, ref havokCursor);
                return false;
            }

            Guid memberFileGuid = blueprintRef.Type == PointerRefType.External
                ? blueprintRef.External.FileGuid
                : Guid.Empty;

            OpenWorldPart target = PickWorldPart(folderParts, openParts, memberFileGuid, group, subWorldAsset);
            if (target == null)
            {
                warnings.Add($"{subWorldName}: no existing WorldPart for {GetBlueprintName(blueprintRef)}");
                ConsumeHavok(member, ref havokCursor);
                return false;
            }

            IList instanceTransforms = member.InstanceTransforms;
            IList instanceVariations = member.InstanceObjectVariation;
            IList renderingOverrides = member.InstanceRenderingOverrides;
            IList radiosityOverrides = member.InstanceRadiosityTypeOverride;
            bool useEbxTransforms = instanceTransforms != null && instanceTransforms.Count > 0;
            IList worldPartObjects = target.Root.Objects;

            if (blueprintRef.Type == PointerRefType.External)
                target.Asset.AddDependency(blueprintRef.External.FileGuid);

            for (int i = 0; i < instanceCount; i++)
            {
                dynamic rod = TypeLibrary.CreateObject("ObjectReferenceObjectData");
                rod.Blueprint = blueprintRef;
                rod.CastSunShadowEnable = true;
                rod.CastReflectionEnable = true;
                rod.CastEnvmapEnable = true;

                object transform;
                if (useEbxTransforms && i < instanceTransforms.Count)
                    transform = instanceTransforms[i];
                else if (havokTransforms != null && havokCursor < havokTransforms.Count)
                    transform = (object)MakeLinearTransform(havokTransforms[havokCursor++]);
                else
                {
                    transform = (object)MakeLinearTransform(Matrix.Identity);
                    if (!useEbxTransforms)
                        havokCursor++;
                }
                SetProp(rod, "BlueprintTransform", transform);

                if (instanceVariations != null && i < instanceVariations.Count)
                {
                    uint hash = Convert.ToUInt32(instanceVariations[i]);
                    if (hash != 0 && variationMap.TryGetValue(hash, out EbxImportReference variation))
                    {
                        rod.ObjectVariation = new PointerRef(variation);
                        target.Asset.AddDependency(variation.FileGuid);
                    }
                }

                if (renderingOverrides != null && i < renderingOverrides.Count)
                    SetProp(rod, "RenderingOverrides", renderingOverrides[i]);
                if (radiosityOverrides != null && i < radiosityOverrides.Count)
                    SetProp(rod, "RadiosityTypeOverride", radiosityOverrides[i]);

                AssignGuid(target.Asset, rod);
                try
                {
                    AssetClassGuid guid = rod.GetInstanceGuid();
                    if (guid.IsExported)
                        rod.Flags = unchecked((uint)guid.ExportedGuid.GetHashCode());
                }
                catch
                {
                }

                target.Asset.AddObject(rod);
                worldPartObjects.Add(new PointerRef(rod));
                result.InstancesCreated++;
            }

            target.Dirty = true;
            return true;
        }

        static OpenWorldPart PickWorldPart(
            List<EbxAssetEntry> folderParts,
            Dictionary<Guid, OpenWorldPart> openParts,
            Guid memberFileGuid,
            dynamic group,
            EbxAsset subWorldAsset)
        {
            if (memberFileGuid != Guid.Empty)
            {
                foreach (EbxAssetEntry candidate in folderParts)
                {
                    OpenWorldPart opened = OpenPart(candidate, openParts);
                    if (WorldPartHasBlueprint(opened, memberFileGuid))
                        return opened;
                }
            }

            foreach (Guid fileGuid in WorldPartsLinkedByGroupEvents(group, subWorldAsset))
            {
                foreach (EbxAssetEntry candidate in folderParts)
                {
                    if (candidate.Guid == fileGuid)
                        return OpenPart(candidate, openParts);
                }

                EbxAssetEntry linked = App.AssetManager.GetEbxEntry(fileGuid);
                if (linked != null && TypeLibrary.IsSubClassOf(linked.Type, "WorldPartData"))
                    return OpenPart(linked, openParts);
            }

            EbxAssetEntry best = null;
            int bestScore = int.MinValue;
            foreach (EbxAssetEntry candidate in folderParts)
            {
                int score = ScoreWorldPart(candidate, openParts);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = candidate;
                }
            }

            return best != null ? OpenPart(best, openParts) : null;
        }

        static int ScoreWorldPart(EbxAssetEntry candidate, Dictionary<Guid, OpenWorldPart> openParts)
        {
            string name = candidate.Filename.ToLowerInvariant();
            int score = 0;
            if (name.Contains("collision") || name.Contains("occluder"))
                score -= 100;
            if (name.Contains("props") || name.Contains("vegetation") || name.Contains("backdrop") || name.Contains("art"))
                score += 50;

            OpenWorldPart opened = OpenPart(candidate, openParts);
            IList objects = opened.Root.Objects;
            if (objects != null)
            {
                foreach (PointerRef pr in objects)
                {
                    if (pr.Type == PointerRefType.Internal && pr.Internal != null &&
                        TypeLibrary.IsSubClassOf(pr.Internal, "ObjectReferenceObjectData"))
                    {
                        score += 20;
                        break;
                    }
                }
            }

            return score;
        }

        static bool WorldPartHasBlueprint(OpenWorldPart part, Guid fileGuid)
        {
            IList objects = part.Root.Objects;
            if (objects == null)
                return false;

            foreach (PointerRef pr in objects)
            {
                object obj = pr.Type == PointerRefType.Internal ? pr.Internal : null;
                if (obj == null || !TypeLibrary.IsSubClassOf(obj, "ReferenceObjectData"))
                    continue;
                PointerRef blueprint = (PointerRef)((dynamic)obj).Blueprint;
                if (blueprint.Type == PointerRefType.External && blueprint.External.FileGuid == fileGuid)
                    return true;
            }
            return false;
        }

        static HashSet<Guid> WorldPartsLinkedByGroupEvents(dynamic group, EbxAsset subWorldAsset)
        {
            HashSet<Guid> result = new HashSet<Guid>();
            IList connections = ((dynamic)subWorldAsset.RootObject).EventConnections;
            if (connections == null)
                return result;

            foreach (dynamic conn in connections)
            {
                PointerRef source = (PointerRef)conn.Source;
                PointerRef target = (PointerRef)conn.Target;
                if (source.Type == PointerRefType.Internal && source.Internal == (object)group && target.Type == PointerRefType.External)
                    result.Add(target.External.FileGuid);
                else if (target.Type == PointerRefType.Internal && target.Internal == (object)group && source.Type == PointerRefType.External)
                    result.Add(source.External.FileGuid);
            }
            return result;
        }

        static OpenWorldPart OpenPart(EbxAssetEntry entry, Dictionary<Guid, OpenWorldPart> openParts)
        {
            if (openParts.TryGetValue(entry.Guid, out OpenWorldPart existing))
                return existing;

            EbxAsset asset = App.AssetManager.GetEbx(entry);
            OpenWorldPart part = new OpenWorldPart
            {
                Entry = entry,
                Asset = asset,
                Root = asset.RootObject
            };
            openParts[entry.Guid] = part;
            return part;
        }

        static void SetProp(object target, string name, object value)
        {
            if (target == null || value == null)
                return;
            PropertyInfo prop = target.GetType().GetProperty(name);
            if (prop == null || !prop.CanWrite)
                return;
            prop.SetValue(target, value);
        }

        static void ConsumeHavok(dynamic member, ref int havokCursor)
        {
            IList instanceTransforms = member.InstanceTransforms;
            if (instanceTransforms != null && instanceTransforms.Count > 0)
                return;
            havokCursor += (int)(uint)member.InstanceCount;
        }

        static List<Matrix> LoadHavokTransformsForGroup(dynamic group, List<string> warnings, string name)
        {
            ulong rid = FindGroupHavokRid(group);
            if (rid == 0)
                return new List<Matrix>();

            ResAssetEntry resEntry = App.AssetManager.GetResEntry(rid);
            if (resEntry == null)
            {
                warnings.Add($"{name}: GroupHavok RES {rid:X} not found");
                return new List<Matrix>();
            }

            using (Stream stream = App.AssetManager.GetRes(resEntry))
            {
                if (stream == null)
                    return new List<Matrix>();
                try
                {
                    return HavokCompoundReader.ReadInstanceTransforms(stream);
                }
                catch (Exception ex)
                {
                    warnings.Add($"{name}: Havok parse failed ({ex.Message})");
                    return new List<Matrix>();
                }
            }
        }

        static object ResolveAny(PointerRef pr)
        {
            if (pr.Type == PointerRefType.Internal)
                return pr.Internal;
            if (pr.Type == PointerRefType.External)
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(pr.External.FileGuid);
                if (entry == null)
                    return null;
                EbxAsset asset = App.AssetManager.GetEbx(entry);
                return asset.GetObject(pr.External.ClassGuid) ?? asset.RootObject;
            }
            return null;
        }

        static ulong FindGroupHavokRid(dynamic group)
        {
            IList components = group.Components;
            if (components == null)
                return 0;

            foreach (PointerRef componentRef in components)
            {
                object component = ResolveAny(componentRef);
                if (component == null || !TypeLibrary.IsSubClassOf(component, "StaticModelGroupPhysicsComponentData"))
                    continue;

                dynamic physics = component;
                IList bodies = physics.PhysicsBodies;
                if (bodies == null)
                    continue;
                foreach (PointerRef bodyRef in bodies)
                {
                    object body = ResolveAny(bodyRef);
                    if (body == null || !TypeLibrary.IsSubClassOf(body, "GroupRigidBodyData"))
                        continue;
                    PointerRef assetRef = (PointerRef)((dynamic)body).Asset;
                    object havok = ResolveAny(assetRef);
                    if (havok == null)
                        continue;
                    if (TypeLibrary.IsSubClassOf(havok, "HavokAsset") || TypeLibrary.IsSubClassOf(havok, "GroupHavokAsset"))
                    {
                        ResourceRef resource = (ResourceRef)((dynamic)havok).Resource;
                        return (ulong)resource;
                    }
                }
            }
            return 0;
        }

        static PointerRef ResolveBlueprintRef(PointerRef memberType, EbxAsset host)
        {
            if (memberType.Type == PointerRefType.External)
            {
                EbxAssetEntry bpEntry = App.AssetManager.GetEbxEntry(memberType.External.FileGuid);
                if (bpEntry == null)
                    return new PointerRef();
                EbxAsset bpAsset = App.AssetManager.GetEbx(bpEntry);
                host?.AddDependency(bpEntry.Guid);
                return new PointerRef(new EbxImportReference
                {
                    FileGuid = bpEntry.Guid,
                    ClassGuid = bpAsset.RootInstanceGuid
                });
            }

            if (memberType.Type == PointerRefType.Internal && memberType.Internal != null &&
                TypeLibrary.IsSubClassOf(memberType.Internal, "Blueprint"))
                return memberType;

            return new PointerRef();
        }

        static string GetBlueprintName(PointerRef blueprintRef)
        {
            if (blueprintRef.Type == PointerRefType.External)
            {
                EbxAssetEntry entry = App.AssetManager.GetEbxEntry(blueprintRef.External.FileGuid);
                if (entry != null)
                    return entry.Filename;
            }
            return "StaticModel";
        }

        static object ResolvePointer(PointerRef pr, EbxAsset asset)
        {
            if (pr.Type == PointerRefType.Internal)
                return pr.Internal;
            if (pr.Type == PointerRefType.External)
                return asset.GetObject(pr.External.ClassGuid);
            return null;
        }

        static void AssignGuid(EbxAsset asset, dynamic obj)
        {
            AssetClassGuid guid = new AssetClassGuid(Utils.GenerateDeterministicGuid(asset.Objects, (Type)obj.GetType(), asset.FileGuid), -1);
            obj.SetInstanceGuid(guid);
        }

        static dynamic MakeLinearTransform(Matrix m)
        {
            dynamic lt = TypeLibrary.CreateObject("LinearTransform");
            lt.right = MakeVec3(m.M11, m.M12, m.M13);
            lt.up = MakeVec3(m.M21, m.M22, m.M23);
            lt.forward = MakeVec3(m.M31, m.M32, m.M33);
            lt.trans = MakeVec3(m.M41, m.M42, m.M43);
            return lt;
        }

        static dynamic MakeVec3(float x, float y, float z)
        {
            dynamic v = TypeLibrary.CreateObject("Vec3");
            v.x = x;
            v.y = y;
            v.z = z;
            return v;
        }

        static void CollectGroupInternals(dynamic group, HashSet<object> removed)
        {
            IList components = group.Components;
            if (components == null)
                return;

            foreach (PointerRef componentRef in components)
            {
                if (componentRef.Type != PointerRefType.Internal || componentRef.Internal == null)
                    continue;
                removed.Add(componentRef.Internal);
                if (!TypeLibrary.IsSubClassOf(componentRef.Internal, "StaticModelGroupPhysicsComponentData"))
                    continue;

                dynamic physics = componentRef.Internal;
                IList bodies = physics.PhysicsBodies;
                if (bodies == null)
                    continue;
                foreach (PointerRef bodyRef in bodies)
                {
                    if (bodyRef.Type != PointerRefType.Internal || bodyRef.Internal == null)
                        continue;
                    removed.Add(bodyRef.Internal);
                    try
                    {
                        PointerRef assetRef = (PointerRef)((dynamic)bodyRef.Internal).Asset;
                        if (assetRef.Type == PointerRefType.Internal && assetRef.Internal != null)
                            removed.Add(assetRef.Internal);
                    }
                    catch
                    {
                    }
                }
            }
        }

        static void StripConnections(dynamic subWorld, HashSet<object> removed)
        {
            StripList(subWorld.EventConnections, removed);
            StripList(subWorld.PropertyConnections, removed);
            StripList(subWorld.LinkConnections, removed);
        }

        static void StripList(IList list, HashSet<object> removed)
        {
            if (list == null)
                return;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                dynamic conn = list[i];
                if (PointsToRemoved(conn.Source, removed) || PointsToRemoved(conn.Target, removed))
                    list.RemoveAt(i);
            }
        }

        static bool PointsToRemoved(PointerRef pr, HashSet<object> removed)
        {
            return pr.Type == PointerRefType.Internal && pr.Internal != null && removed.Contains(pr.Internal);
        }
    }
}
