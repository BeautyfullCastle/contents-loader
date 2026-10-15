using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace H.ContentsLoader
{
    internal sealed class ContentCatalog
    {
        public const string FileName = "catalog.bin";
        const int FormatVersion = 1;
        static readonly byte[] Magic = { (byte)'H', (byte)'C', (byte)'L', (byte)'C' };

        public struct Pack
        {
            public string FileName;
            public ContentDelivery Delivery;
            public long Size;
            // 전이 의존성 전체임. 런타임에서 다시 펼치지 않음
            public int[] Dependencies;
        }

        public struct Entry
        {
            public ulong PathHash;
            public int Pack;
            public string AssetName;
            public ContentDelivery Delivery;
        }

        public struct Group
        {
            public ulong FolderHash;
            public int[] Packs;
        }

        public long BuildTimestamp;
        public string RemoteUrl = "";
        public long MemoryBudget = long.MaxValue;
        public Pack[] Packs = Array.Empty<Pack>();
        public Entry[] Entries = Array.Empty<Entry>();
        public Group[] Groups = Array.Empty<Group>();

        public bool TryFindEntry(string path, out Entry entry)
        {
            var index = BinarySearch(Entries, ContentPath.Hash(path), e => e.PathHash);
            entry = index >= 0 ? Entries[index] : default;
            return index >= 0;
        }

        public bool TryFindGroup(string folder, out Group group)
        {
            var index = BinarySearch(Groups, ContentPath.Hash(folder), g => g.FolderHash);
            group = index >= 0 ? Groups[index] : default;
            return index >= 0;
        }

        public void Sort()
        {
            Array.Sort(Entries, (a, b) => a.PathHash.CompareTo(b.PathHash));
            Array.Sort(Groups, (a, b) => a.FolderHash.CompareTo(b.FolderHash));
        }

        static int BinarySearch<T>(T[] items, ulong key, Func<T, ulong> keyOf)
        {
            int lo = 0, hi = items.Length - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) >> 1;
                var k = keyOf(items[mid]);
                if (k == key) return mid;
                if (k < key) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        public byte[] ToBytes()
        {
            using var stream = new MemoryStream();
            using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
            {
                w.Write(Magic);
                w.Write(FormatVersion);
                w.Write(BuildTimestamp);
                w.Write(RemoteUrl ?? "");
                w.Write(MemoryBudget);

                w.Write(Packs.Length);
                foreach (var p in Packs)
                {
                    w.Write(p.FileName);
                    w.Write((byte)p.Delivery);
                    w.Write(p.Size);
                    WriteInts(w, p.Dependencies);
                }

                w.Write(Entries.Length);
                foreach (var e in Entries)
                {
                    w.Write(e.PathHash);
                    w.Write(e.Pack);
                    w.Write(e.AssetName);
                    w.Write((byte)e.Delivery);
                }

                w.Write(Groups.Length);
                foreach (var g in Groups)
                {
                    w.Write(g.FolderHash);
                    WriteInts(w, g.Packs);
                }
            }
            return stream.ToArray();
        }

        public static ContentCatalog FromBytes(byte[] bytes)
        {
            using var r = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
            var magic = r.ReadBytes(Magic.Length);
            for (var i = 0; i < Magic.Length; i++)
                if (magic.Length != Magic.Length || magic[i] != Magic[i])
                    throw new InvalidDataException("Not a Contents Loader catalog.");

            var version = r.ReadInt32();
            if (version != FormatVersion)
                throw new InvalidDataException($"Unsupported catalog version {version}.");

            var catalog = new ContentCatalog
            {
                BuildTimestamp = r.ReadInt64(),
                RemoteUrl = r.ReadString(),
                MemoryBudget = r.ReadInt64(),
            };

            catalog.Packs = new Pack[r.ReadInt32()];
            for (var i = 0; i < catalog.Packs.Length; i++)
            {
                catalog.Packs[i] = new Pack
                {
                    FileName = r.ReadString(),
                    Delivery = (ContentDelivery)r.ReadByte(),
                    Size = r.ReadInt64(),
                    Dependencies = ReadInts(r),
                };
            }

            catalog.Entries = new Entry[r.ReadInt32()];
            for (var i = 0; i < catalog.Entries.Length; i++)
            {
                catalog.Entries[i] = new Entry
                {
                    PathHash = r.ReadUInt64(),
                    Pack = r.ReadInt32(),
                    AssetName = r.ReadString(),
                    Delivery = (ContentDelivery)r.ReadByte(),
                };
            }

            catalog.Groups = new Group[r.ReadInt32()];
            for (var i = 0; i < catalog.Groups.Length; i++)
            {
                catalog.Groups[i] = new Group
                {
                    FolderHash = r.ReadUInt64(),
                    Packs = ReadInts(r),
                };
            }
            return catalog;
        }

        static void WriteInts(BinaryWriter w, IReadOnlyCollection<int> values)
        {
            values ??= Array.Empty<int>();
            w.Write(values.Count);
            foreach (var v in values) w.Write(v);
        }

        static int[] ReadInts(BinaryReader r)
        {
            var values = new int[r.ReadInt32()];
            for (var i = 0; i < values.Length; i++) values[i] = r.ReadInt32();
            return values;
        }
    }
}
