using System;
using TMPro;
using UnityEngine;

namespace CPMod_Multiplayer.LobbyManagement
{
    internal class ErrorWindow : MonoBehaviour
    {
        public delegate void DlgOnClose();
            

        public DlgOnClose OnClose;
        
        private TextMeshProUGUI message;

        private void Initialize()
        {
            message = transform.Find("Base/Text_ErrorMsg")?.GetComponent<TextMeshProUGUI>();
            var closeButton = WindowHelpers.FindCloseButton(gameObject);
            closeButton.onClick.AddListener(() => OnClose?.Invoke());

            var root = gameObject;
            OnClose = () => WindowHelpers.DefaultOnClose(root);
        }

        private void OnDestroy()
        {
            Mod.logger.Log("=== ErrorWindow OnDestroy:\n" + StackTraceUtility.ExtractStackTrace());
        }

        internal static ErrorWindow Show(string message)
        {
            var root = GameObject.Find("EventSystem")?.transform?.parent;
            Mod.logger.Log($"Showing error window: {message} at root {root}");
            var errorWindowObj = Instantiate(Mod.assetBundle.LoadAsset<GameObject>("MultiplayerErrorWindow"), root, false);
            var instance = errorWindowObj.AddComponent<ErrorWindow>();
            instance.Initialize();
            
            
            Mod.logger.Log($"instance=null? {instance == null}");
            Mod.logger.Log($"instance message: {instance.message} gameObject {instance.gameObject}");
            instance.message.text = message;
            instance.gameObject.SetActive(true);
            Mod.logger.Log($"SoundEffectManager: {SoundEffectManager.Instance}");
            SoundEffectManager.Instance.PlayOneShot("se_ok");

            if (MultiplayerLobbyWindow.Instance != null)
            {
                Destroy(MultiplayerLobbyWindow.Instance.gameObject);
            }

            if (MultiplayerSetupWindow.Instance != null)
            {
                Destroy(MultiplayerSetupWindow.Instance.gameObject);
            }

            Time.timeScale = 0.0f;
            
            return instance;
        }
    }
}