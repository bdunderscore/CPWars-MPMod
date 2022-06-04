using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class MultiplayerSetupWindow : MonoBehaviour
    {
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
            roomNumberField = transform.Find("Base/UI_Controls/Input_RoomNumber").GetComponent<TMP_InputField>();

            var top = transform.parent;

            WindowHelpers.SetCloseButton(gameObject);
            
            transform.Find("Base/UI_Controls/Button_Create").GetComponent<Button>()
                .onClick.AddListener(OnCreateRoom);
            transform.Find("Base/UI_Controls/Button_Join").GetComponent<Button>()
                .onClick.AddListener(OnJoinRoom);

            //var digitValidator = ScriptableObject.CreateInstance<TMP_DigitValidator>();
            //roomNumberField.inputValidator = digitValidator;
        }

        void OnCreateRoom()
        {
            var lobby = HostedLobby.CreateLobby();

            ConnectToLobby(lobby);
        }

        void OnJoinRoom()
        {
            var address = transform.Find("Base/UI_Controls/Input_RoomNumber").GetComponent<TMP_InputField>().text;
            Lobby lobby = RemoteLobby.ConnectLobby(address);
            
            ConnectToLobby(lobby);
        }
        
        private void ConnectToLobby(Lobby lobby)
        {
            transform.Find("Base/UI_Controls").gameObject.SetActive(false);
            transform.Find("Base/Text_Connecting").gameObject.SetActive(true);

            lobby.OnError += (msg) =>
            {
                gameObject.SetActive(false);
                ErrorWindow.Show(msg);
                Destroy(lobby.gameObject);
            };

            lobby.OnStateChange += () =>
            {
                if (lobby.State != LobbyState.JOINING)
                {
                    GUIUtility.systemCopyBuffer = lobby.LobbyAddress;
                    gameObject.SetActive(false);
                    MultiplayerLobbyWindow.Create(lobby);
                }
            };
        }
    }
}