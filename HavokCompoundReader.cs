using FrostySdk;
using FrostySdk.IO;
using SharpDX;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NoHavokSplitPlugin
{
    internal static class NativeReaderHkxExtensions
    {
        public static string ReadTerminatedString(this NativeReader reader, byte terminate)
        {
            string result = "";
            while (true)
            {
                char c = (char)reader.ReadByte();
                if (c == terminate)
                    return result;
                if (c == 0x00)
                    continue;
                result += c;
            }
        }
    }

    namespace Hkx
    {
        public class hkBaseClass
        {
            public long Offset;
            public virtual void Serialize(NativeReader reader, HkxHeader header)
            {
            }
        }

        public class hknpShape : hkBaseClass
        {
            public int unknown;
            public hkRefCountedProperties Properties;

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                reader.Position += 0x11;
                unknown = reader.ReadByte();
                reader.Position += 0x0E;
                Properties = header.ReadObject(reader) as hkRefCountedProperties;
            }
        }

        public class hknpCapsuleShape : hknpShape { }
        public class hknpConvexPolytopeShape : hknpShape { }
        public class hknpSphereShape : hknpShape { }
        public class hknpCompressedMeshShape : hknpShape { }
        public class hknpCompressedMeshShapeData : hkBaseClass { }

        public class hknpStaticCompoundShape : hknpShape
        {
            public class hknpInstance
            {
                public Matrix Transform;
                public Vector3 Scale;
                public hkBaseClass shape;
                public long DataOffset { get; set; }

                public void Serialize(NativeReader reader, HkxHeader header)
                {
                    DataOffset = reader.Position;

                    Matrix m = new Matrix { M11 = reader.ReadFloat(), M12 = reader.ReadFloat(), M13 = reader.ReadFloat() };
                    reader.ReadUInt();
                    m.M21 = reader.ReadFloat(); m.M22 = reader.ReadFloat(); m.M23 = reader.ReadFloat(); reader.ReadUInt();
                    m.M31 = reader.ReadFloat(); m.M32 = reader.ReadFloat(); m.M33 = reader.ReadFloat(); reader.ReadUInt();
                    m.M41 = reader.ReadFloat(); m.M42 = reader.ReadFloat(); m.M43 = reader.ReadFloat(); reader.ReadUInt();
                    Transform = m;

                    Scale = new Vector3(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());
                    reader.ReadUInt();

                    shape = header.ReadObject(reader);
                    for (int i = 0; i < 10; i++)
                        reader.ReadUInt();
                }
            }

            public List<hknpInstance> Instances = new List<hknpInstance>();

            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                base.Serialize(reader, header);

                reader.Position += 0x38;
                long arrayOffset = header.ReadArray(reader);
                int numInstances = reader.ReadInt();

                reader.Position += 0x14;
                reader.Position += 32;
                reader.Position += 0x20;
                header.ReadObject(reader);

                reader.Position = arrayOffset;
                for (int i = 0; i < numInstances; i++)
                {
                    hknpInstance inst = new hknpInstance();
                    inst.Serialize(reader, header);
                    Instances.Add(inst);
                }
            }
        }

        public class hknpStaticCompoundShapeData : hkBaseClass { }

        public class hkRefCountedProperties : hkBaseClass
        {
            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                long arrayOffset = header.ReadArray(reader);
                int numItems = reader.ReadInt();
                reader.Position = arrayOffset;
                for (int i = 0; i < numItems; i++)
                    header.ReadObject(reader);
            }
        }

        public class hknpShapeMassProperties : hkBaseClass { }

        public class HavokPhysicsContainer : hkBaseClass
        {
            public override void Serialize(NativeReader reader, HkxHeader header)
            {
                reader.Position += 0x10;
                long arrayOffset = header.ReadArray(reader);
                int numItems = reader.ReadInt();
                reader.Position = arrayOffset;
                for (int i = 0; i < numItems; i++)
                    header.ReadObject(reader);
            }
        }
    }

    public class HkxHeader
    {
        public uint StartOffset;
        public uint Magic1;
        public uint Magic2;
        public uint UserTag;
        public uint FileVersion;
        public uint LayoutRules;
        public uint NumSections;
        public uint ContentsSectionIndex;
        public uint ContentsSectionOffset;
        public uint ContentsClassNameSectionIndex;
        public uint ContentsClassNameSectionOffset;
        public string ContentsVersion;
        public uint Flags;
        public uint Padding;
        public int Bits;

        public Dictionary<long, long> ArrayOffsets = new Dictionary<long, long>();
        public Dictionary<long, Hkx.hkBaseClass> ObjectOffsets = new Dictionary<long, Hkx.hkBaseClass>();

        public void Serialize(NativeReader reader, int bits)
        {
            Bits = bits;
            Magic1 = reader.ReadUInt();
            if (Magic1 != 0x57e0e057)
            {
                do
                {
                    reader.BaseStream.Seek(-3, SeekOrigin.Current);
                    Magic1 = reader.ReadUInt();
                }
                while (Magic1 != 0x57e0e057);
            }

            StartOffset = (uint)reader.BaseStream.Position - 4;
            Magic2 = reader.ReadUInt();
            if (Magic2 == 0x10c0c010)
            {
                UserTag = reader.ReadUInt();
                FileVersion = reader.ReadUInt();
                LayoutRules = reader.ReadUInt();
                NumSections = reader.ReadUInt();
                ContentsSectionIndex = reader.ReadUInt();
                ContentsSectionOffset = reader.ReadUInt();
                ContentsClassNameSectionIndex = reader.ReadUInt();
                ContentsClassNameSectionOffset = reader.ReadUInt();
                ContentsVersion = reader.ReadTerminatedString(0xff);
                Flags = reader.ReadUInt();
                Padding = reader.ReadUInt();
            }
        }

        public Hkx.hkBaseClass ReadObject(NativeReader reader)
        {
            reader.ReadInt();
            if (Bits == 64)
                reader.ReadInt();

            long key = reader.Position - (Bits == 64 ? 8 : 4);
            if (!ObjectOffsets.ContainsKey(key))
                return null;
            return ObjectOffsets[key];
        }

        public long ReadArray(NativeReader reader)
        {
            long arrayOffset = ArrayOffsets[reader.Position];
            reader.ReadInt();
            if (Bits == 64)
                reader.ReadInt();
            return arrayOffset;
        }
    }

    internal class HkxInstance
    {
        public class HkxClassDescriptor
        {
            public uint Signature;
            public byte Version;
            public string Name;

            public void Serialize(NativeReader reader)
            {
                Signature = reader.ReadUInt();
                Version = reader.ReadByte();
                Name = reader.ReadNullTerminatedString();
            }
        }

        public HkxHeader Header { get; set; }
        public List<Hkx.hkBaseClass> Objects => objList.Values.ToList();

        public int LocalFixupSize;
        public int GlobalFixupSize;
        public int AbsoluteDataStart;
        int virtualFixupsOffset;
        public int endOffset;

        Dictionary<ulong, HkxClassDescriptor> Classes = new Dictionary<ulong, HkxClassDescriptor>();
        Dictionary<long, Hkx.hkBaseClass> objList = new Dictionary<long, Hkx.hkBaseClass>();

        static Hkx.hkBaseClass CreateHkx(string name)
        {
            switch (name)
            {
                case "hknpStaticCompoundShape": return new Hkx.hknpStaticCompoundShape();
                case "hknpStaticCompoundShapeData": return new Hkx.hknpStaticCompoundShapeData();
                case "hknpShape": return new Hkx.hknpShape();
                case "hknpConvexPolytopeShape": return new Hkx.hknpConvexPolytopeShape();
                case "hknpCapsuleShape": return new Hkx.hknpCapsuleShape();
                case "hknpSphereShape": return new Hkx.hknpSphereShape();
                case "hknpCompressedMeshShape": return new Hkx.hknpCompressedMeshShape();
                case "hknpCompressedMeshShapeData": return new Hkx.hknpCompressedMeshShapeData();
                case "hkRefCountedProperties": return new Hkx.hkRefCountedProperties();
                case "hknpShapeMassProperties": return new Hkx.hknpShapeMassProperties();
                case "HavokPhysicsContainer": return new Hkx.HavokPhysicsContainer();
                default: return new Hkx.hkBaseClass();
            }
        }

        public void Serialize(NativeReader reader, int bits)
        {
            Header = new HkxHeader();
            Header.Serialize(reader, bits);

            int ClassSectionOffset = 0;
            int ClassSectionSize = 0;
            int CurrentSection = 0;

            while (CurrentSection < Header.NumSections)
            {
                string BlockType = reader.ReadTerminatedString(0xff);
                if (BlockType.Contains("__classnames__"))
                {
                    ClassSectionOffset = reader.ReadInt();
                    reader.ReadLong();
                    ClassSectionSize = reader.ReadInt();
                    reader.BaseStream.Seek(12, SeekOrigin.Current);
                    CurrentSection++;
                }
                else if (BlockType.Contains("__types__"))
                {
                    reader.ReadInt();
                    reader.BaseStream.Seek(24, SeekOrigin.Current);
                    CurrentSection++;
                }
                else if (BlockType.Contains("__data__"))
                {
                    AbsoluteDataStart = reader.ReadInt();
                    LocalFixupSize = reader.ReadInt();
                    GlobalFixupSize = reader.ReadInt();
                    virtualFixupsOffset = reader.ReadInt();
                    reader.ReadInt();
                    reader.ReadInt();
                    endOffset = reader.ReadInt();
                    CurrentSection++;
                }
            }

            reader.Position = Header.StartOffset + ClassSectionOffset;
            while (reader.BaseStream.Position < (Header.StartOffset + ClassSectionOffset + ClassSectionSize))
            {
                ulong ClassOffset = (ulong)(reader.BaseStream.Position - (Header.StartOffset + ClassSectionOffset));
                HkxClassDescriptor Class = new HkxClassDescriptor();
                Class.Serialize(reader);
                Classes[ClassOffset + 5] = Class;
            }

            reader.Position = Header.StartOffset + AbsoluteDataStart + virtualFixupsOffset;
            int TotalSize = (int)Header.StartOffset + AbsoluteDataStart + endOffset;

            while (reader.BaseStream.Position < TotalSize)
            {
                long DataOffset = reader.ReadLong();
                int ClassType = reader.ReadInt();

                if (ClassType != -1 && reader.BaseStream.Position < TotalSize)
                {
                    HkxClassDescriptor CurrentClassDescriptor = Classes[(ulong)ClassType];
                    Hkx.hkBaseClass BaseClass = CreateHkx(CurrentClassDescriptor.Name);
                    BaseClass.Offset = Header.StartOffset + AbsoluteDataStart + DataOffset;
                    if (!objList.ContainsKey(BaseClass.Offset))
                        objList.Add(BaseClass.Offset, BaseClass);
                }
            }

            reader.Position = Header.StartOffset + AbsoluteDataStart + endOffset;
        }

        public void ReadObjects(NativeReader reader)
        {
            foreach (long offset in objList.Keys.ToList())
            {
                Hkx.hkBaseClass value = objList[offset];
                if (value != null)
                {
                    reader.Position = offset;
                    value.Serialize(reader, Header);
                }
            }
        }

        public Hkx.hkBaseClass GetObject(long offset)
        {
            if (!objList.ContainsKey(offset))
                return null;
            return objList[offset];
        }
    }

    /// <summary>
    /// Reads GroupHavokAsset.Resource the same way LevelEditor HavokPhysicsData.Read does,
    /// then returns instance world matrices (preScale * Scaling * Transform).
    /// </summary>
    internal static class HavokCompoundReader
    {
        public static List<Matrix> ReadInstanceTransforms(Stream resStream)
        {
            List<Matrix> result = new List<Matrix>();
            if (resStream == null)
                return result;

            using (NativeReader reader = new NativeReader(resStream))
            {
                reader.ReadInt(); // partCount
                int partTranslationsCount = reader.ReadInt();
                reader.ReadLong();
                int localAabbsCount = reader.ReadInt();
                reader.ReadLong();
                int materialIndicesCount = reader.ReadInt();
                reader.ReadLong();
                int materialFlagsAndIndicesCount = reader.ReadInt();
                reader.ReadLong();

                int unknownCount = 0;
                if (ProfilesLibrary.DataVersion != 20141118 && ProfilesLibrary.DataVersion != 20141117)
                {
                    unknownCount = reader.ReadInt();
                    reader.ReadLong();
                }

                reader.ReadByte();
                reader.ReadByte();
                reader.ReadBytes(2);
                reader.Pad(16);

                reader.Position += partTranslationsCount * 16;
                reader.Pad(16);
                reader.Position += localAabbsCount * 32;
                reader.Pad(16);
                reader.Position += materialIndicesCount;
                reader.Pad(16);
                reader.Position += materialFlagsAndIndicesCount * 4;
                reader.Pad(16);

                if (ProfilesLibrary.DataVersion != 20141118 && ProfilesLibrary.DataVersion != 20141117)
                {
                    reader.Position += unknownCount * 2;
                    reader.Pad(16);
                }

                HkxInstance inst32 = new HkxInstance();
                inst32.Serialize(reader, 32);

                HkxInstance inst64 = new HkxInstance();
                if (ProfilesLibrary.DataVersion != 20140225)
                    inst64.Serialize(reader, 64);

                int fixupSize32 = reader.ReadInt();
                int fixupSize64 = reader.ReadInt();

                long pos = reader.Position;
                long fileOffset = inst32.Header.StartOffset + inst32.AbsoluteDataStart;

                while (reader.Position < (pos + inst32.GlobalFixupSize))
                {
                    int offset = reader.ReadInt();
                    int objOffset = reader.ReadInt();
                    if (offset != -1)
                        inst32.Header.ArrayOffsets[fileOffset + offset] = fileOffset + objOffset;
                }
                while (reader.Position < (pos + inst32.LocalFixupSize - inst32.GlobalFixupSize))
                {
                    int offset = reader.ReadInt();
                    reader.ReadInt();
                    int objOffset = reader.ReadInt();
                    inst32.Header.ObjectOffsets[fileOffset + offset] = inst32.GetObject(fileOffset + objOffset);
                }

                reader.Position = (pos + fixupSize32);
                pos = reader.Position;
                fileOffset = inst64.Header.StartOffset + inst64.AbsoluteDataStart;

                while (reader.Position < (pos + inst64.GlobalFixupSize))
                {
                    int offset = reader.ReadInt();
                    int objOffset = reader.ReadInt();
                    if (offset != -1)
                        inst64.Header.ArrayOffsets[fileOffset + offset] = fileOffset + objOffset;
                }
                pos = reader.Position;
                while (reader.Position < (pos + (inst64.LocalFixupSize - inst64.GlobalFixupSize)))
                {
                    int offset = reader.ReadInt();
                    reader.ReadInt();
                    int objOffset = reader.ReadInt();
                    inst64.Header.ObjectOffsets[fileOffset + offset] = inst64.GetObject(fileOffset + objOffset);
                }

                inst64.ReadObjects(reader);

                Hkx.hknpStaticCompoundShape root = inst64.Objects.OfType<Hkx.hknpStaticCompoundShape>().FirstOrDefault();
                if (root == null)
                    return result;

                for (int i = 0; i < root.Instances.Count; i++)
                {
                    Hkx.hknpStaticCompoundShape.hknpInstance instance = root.Instances[i];
                    Matrix preScale = Matrix.Identity;
                    Hkx.hknpShape shape = instance.shape as Hkx.hknpShape;
                    if (shape != null && (shape.unknown & 0x10) != 0)
                        preScale = Matrix.Scaling(-1, 1, 1);

                    result.Add(preScale * Matrix.Scaling(instance.Scale) * instance.Transform);
                }
            }

            return result;
        }
    }
}
