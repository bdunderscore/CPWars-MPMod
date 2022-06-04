using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    internal static class WindowHelpers
    {
        internal static Transform CanvasRoot => GameObject.Find("EventSystem").transform.parent;
        
        internal static Button SetCloseButton(GameObject root)
        {
            var closeButton = root.transform.Find("Base/Button_Close").GetComponent<Button>();
            closeButton.onClick.AddListener(() =>
            {
                SoundEffectManager.Instance.PlayOneShot("se_out");
                root.transform.parent.Find("Top").gameObject.SetActive(true);
                UnityEngine.Object.Destroy(root.gameObject);
            });
            return closeButton;
        } 
    }
}