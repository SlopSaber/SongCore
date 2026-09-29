using System;
using MonoMod.RuntimeDetour;
using UnityEngine.Networking;
using Zenject;

namespace SongCore.Hooks
{
    /// <summary>
    /// Workaround for <see href="https://github.com/Meivyn/BeatSaberBugs/issues/28"/>.
    /// </summary>
    internal class EscapeURLHook : IInitializable, IDisposable
    {
        private Hook _getEscapedURLForFilePathHook = null!;

        public void Initialize()
        {
            _getEscapedURLForFilePathHook = new Hook(typeof(FileHelpers).GetMethod(nameof(FileHelpers.GetEscapedURLForFilePath))!, GetEscapedURL, true);
        }

        public void Dispose()
        {
            _getEscapedURLForFilePathHook.Dispose();
        }

        private string GetEscapedURL(Func<string, string> original, string filePath)
        {
            return FileHelpers.PathIsUrl(filePath) ? filePath : $"file:///{UnityWebRequest.EscapeURL(filePath)}";
        }
    }
}
