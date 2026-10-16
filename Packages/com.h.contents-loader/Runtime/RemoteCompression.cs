namespace H.ContentsLoader
{
    /// <summary>How packs uploaded to the server are compressed. Players always store downloaded packs as LZ4.</summary>
    public enum RemoteCompression : byte
    {
        /// <summary>Smallest download. Slowest build, because changed packs are compressed again by a second bundle build.</summary>
        LZMA = 0,

        /// <summary>About a quarter larger than LZMA. Only changed packs are compressed, in parallel.</summary>
        GZip = 1,

        /// <summary>Largest download, fastest build. The player keeps the file as is.</summary>
        LZ4 = 2,
    }
}
