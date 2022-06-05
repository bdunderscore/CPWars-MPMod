using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    internal static class WindowHelpers
    {
        internal static Transform CanvasRoot => GameObject.Find("EventSystem").transform.parent;
        
        internal static Button SetCloseButton(GameObject root)
        {
            var closeButton = FindCloseButton(root);
            closeButton.onClick.AddListener(() => { DefaultOnClose(root); });
            return closeButton;
        }

        internal static void DefaultOnClose(GameObject root)
        {
            Mod.logger.Log("[[[ DefaultOnClose ]]]");
            SoundEffectManager.Instance.PlayOneShot("se_out");
            root.transform.parent.Find("Top").gameObject.SetActive(true);
            UnityEngine.Object.Destroy(root.gameObject);
        }

        internal static Button FindCloseButton(GameObject root)
        {
            return root.transform.Find("Base/Button_Close")?.GetComponent<Button>();
        }
    }
}