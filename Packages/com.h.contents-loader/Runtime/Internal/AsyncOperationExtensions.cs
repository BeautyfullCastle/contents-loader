using System.Threading.Tasks;
using UnityEngine;

namespace H.ContentsLoader
{
    internal static class AsyncOperationExtensions
    {
        public static Task AsTask(this AsyncOperation operation)
        {
            if (operation.isDone) return Task.CompletedTask;
            var source = new TaskCompletionSource<bool>();
            operation.completed += _ => source.TrySetResult(true);
            return source.Task;
        }
    }
}
