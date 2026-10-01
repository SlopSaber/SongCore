using HMUI;
using UnityEngine;

namespace SongCore.UI
{
    internal static class BasicUI
    {

        internal static Sprite? MissingCharIcon;
        internal static Sprite? LightshowIcon;
        internal static Sprite? ExtraDiffsIcon;
        internal static Sprite? WIPIcon;
        internal static Sprite? FolderIcon;

        internal static void GetIcons()
        {
            if (!MissingCharIcon)
            {
                MissingCharIcon = Utilities.Utils.LoadPreparedIcon("SongCore.Icons.MissingChar.png")!;
            }

            if (!LightshowIcon)
            {
                LightshowIcon = Utilities.Utils.LoadPreparedIcon("SongCore.Icons.Lightshow.png")!;
            }

            if (!ExtraDiffsIcon)
            {
                ExtraDiffsIcon = Utilities.Utils.LoadPreparedIcon("SongCore.Icons.ExtraDiffsIcon.png")!;
            }

            if (!WIPIcon)
            {
                WIPIcon = Utilities.Utils.LoadPreparedIcon("SongCore.Icons.squek.png")!;
            }

            if (!FolderIcon)
            {
                FolderIcon = Utilities.Utils.LoadPreparedIcon("SongCore.Icons.FolderIcon.png")!;
            }
        }
    }
}
