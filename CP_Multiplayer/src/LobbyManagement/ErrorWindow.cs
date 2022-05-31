using TMPro;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    internal class ErrorWindow : MonoBehaviour
    {
        private static ErrorWindow cache;

        private static ErrorWindow Instance
        {
            get
            {
                if (cache == null)
                {
                    var root = GameObject.Find("EventSystem").transform.parent;
                    var errorWindowObj = Instantiate(Mod.assetBundle.LoadAsset<GameObject>("MultiplayerErrorWindow"), root, false);
                    cache = errorWindowObj.AddComponent<ErrorWindow>();
                    cache.Initialize();
                }

                return cache;
            }
        }

        private TextMeshProUGUI message;

        private void Initialize()
        {
            message = transform.Find("Base/Text_ErrorMsg").GetComponent<TextMeshProUGUI>();
            WindowHelpers.SetCloseButton(gameObject);
        }

        internal static void Show(string message)
        {
            var instance = Instance;
            instance.message.text = message;
            Instance.gameObject.SetActive(true);
            SoundEffectManager.Instance.PlayOneShot("se_ok");
        }
    }
}