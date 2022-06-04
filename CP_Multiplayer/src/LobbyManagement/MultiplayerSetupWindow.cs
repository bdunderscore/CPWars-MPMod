using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class MultiplayerSetupWindow : MonoBehaviour
    {
        public static MultiplayerSetupWindow Instance;
        
        private TMP_InputField roomNumberField;

        internal static MultiplayerSetupWindow Create()
        {
            var mpWindowPrefab = Mod.assetBundle.LoadAsset<GameObject>("MultiplayerSetupWindow");
            var root = GameObject.Find("EventSystem").transform.parent;
            var mpWindow = Instantiate(mpWindowPrefab, root);
            mpWindow.SetActive(false);
            return mpWindow.AddComponent<MultiplayerSetupWindow>();
        }
        
        private void Awake()
        {
            Instance = this;
            roomNumberField = transform.Find("Base/UI_Controls/Input_RoomNumber").GetComponent<TMP_InputField>();

            var top = transform.parent;

            WindowHelpers.SetCloseButton(gameObject).onClick.AddListener(() =>
            {
                LobbyManager.CurrentLobby = null;
            });
            
            transform.Find("Base/UI_Controls/Button_Create").GetComponent<Button>()
                .onClick.AddListener(OnCreateRoom);
            transform.Find("Base/UI_Controls/Button_Join").GetComponent<Button>()
                .onClick.AddListener(OnJoinRoom);

            //var digitValidator = ScriptableObject.CreateInstance<TMP_DigitValidator>();
            //roomNumberField.inputValidator = digitValidator;
        }

        private void OnDestroy()
        {
            Instance = null;
        }

        void OnCreateRoom()
        {
            var lobby = HostedLobby.CreateLobby();

            LobbyManager.CurrentLobby = lobby;

            ConnectToLobby(lobby);
        }

        void OnJoinRoom()
        {
            var address = transform.Find("Base/UI_Controls/Input_RoomNumber").GetComponent<TMP_InputField>().text;
            Lobby lobby = RemoteLobby.ConnectLobby(address);
            
            LobbyManager.CurrentLobby = lobby;
            
            ConnectToLobby(lobby);
        }
        
        private void ConnectToLobby(Lobby lobby)
        {
            transform.Find("Base/UI_Controls").gameObject.SetActive(false);
            transform.Find("Base/Text_Connecting").gameObject.SetActive(true);

            Lobby.DelegateOnStateChange callback = null;
            callback = () =>
            {
                if (lobby.State != LobbyState.JOINING)
                {
                    GUIUtility.systemCopyBuffer = lobby.LobbyAddress;
                    gameObject.SetActive(false);
                    MultiplayerLobbyWindow.Create();
                    lobby.OnStateChange -= callback;
                }
            };
            
            lobby.OnStateChange += callback;
        }
    }
}