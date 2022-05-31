using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    internal static class WindowHelpers
    {
        internal static Transform CanvasRoot => GameObject.Find("EventSystem").transform.parent;
        
        internal static void SetCloseButton(GameObject root)
        {
            root.transform.Find("Base/Button_Close").GetComponent<Button>().onClick.AddListener(() =>
            {
                SoundEffectManager.Instance.PlayOneShot("se_out");
                root.SetActive(false);
                root.transform.parent.Find("Top").gameObject.SetActive(true);
            });
        } 
    }
}