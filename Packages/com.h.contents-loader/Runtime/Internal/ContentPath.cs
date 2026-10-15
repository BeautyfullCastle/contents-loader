namespace H.ContentsLoader
{
    internal static class ContentPath
    {
        public static string Normalize(string path) => path.Replace('\\', '/').Trim('/');

        // FNV-1a 64. 대소문자 무시함 (Resources.Load와 같은 규칙)
        public static ulong Hash(string path)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;

            var hash = offset;
            foreach (var c in Normalize(path))
            {
                var lower = char.ToLowerInvariant(c);
                hash ^= (byte)lower;
                hash *= prime;
                hash ^= (byte)(lower >> 8);
                hash *= prime;
            }
            return hash;
        }
    }
}
