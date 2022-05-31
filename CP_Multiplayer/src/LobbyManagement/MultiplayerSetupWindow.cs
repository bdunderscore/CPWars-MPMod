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

            var digitValidator = ScriptableObject.CreateInstance<TMP_DigitValidator>();
            roomNumberField.inputValidator = digitValidator;
        }

        void OnCreateRoom()
        {
            gameObject.SetActive(false);
            MultiplayerLobby.Create().gameObject.SetActive(true);
        }

        void OnJoinRoom()
        {
            gameObject.SetActive(false);
            MultiplayerLobby.Create().gameObject.SetActive(true);
        }
    }
}