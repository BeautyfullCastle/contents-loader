using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace H.ContentsLoader
{
    // 팩 파일을 어디에 두고 어떻게 여는지 감춤. 기기 파일(FilePackStore)과 메모리(MemoryPackStore, WebGL)가 있음
    internal interface IPackStore
    {
        int DownloadCount { get; }

        // 기기에 있어 바로 열 수 있는지
        bool Has(ContentCatalog.Pack pack);

        PackLocation LocationOf(ContentCatalog.Pack pack);

        long RemainingBytesOf(ContentCatalog.Pack pack);

        // 없는 팩만 받음
        Task Fetch(IReadOnlyList<ContentCatalog.Pack> packs, IProgress<float> progress);

        AssetBundle Open(ContentCatalog.Pack pack);

        AssetBundleCreateRequest OpenAsync(ContentCatalog.Pack pack);

        // 연 팩이 메모리에 올리는 바이트(추정)
        long MemoryOf(ContentCatalog.Pack pack);

        // 카탈로그가 더는 쓰지 않는 팩을 지움
        int RemoveUnused(ICollection<string> keepFileNames, ICollection<string> inUse);
    }
}
