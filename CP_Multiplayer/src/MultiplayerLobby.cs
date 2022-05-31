using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.HarmonyPatches;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer
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

    internal class ErrorWindow : MonoBehaviour
    {
        internal static ErrorWindow cache;
        
        internal static ErrorWindow Instance
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
        
        void Initialize()
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
    
    public class MultiplayerLobby : MonoBehaviour
    {
        private GameObject lobbyMemberTemplate;
        private MultiplayerPlayerSlot selfEntry;
        private GameInitializeWindow stubInitWindow;
        private GameObject clubSelect;

        internal static MultiplayerLobby Create()
        {
            try
            {
                var root = GameObject.Find("EventSystem").transform.parent;

                var prefabAsset = Mod.assetBundle.LoadAsset<GameObject>("MultiplayerLobbyWindow");
                Mod.logger.Log($"Loading MultiplayerLobbyWindow from asset bundle: {prefabAsset}");
                var mpLobbyWindow = Instantiate(prefabAsset, root, false).AddComponent<MultiplayerLobby>();
                WindowHelpers.SetCloseButton(mpLobbyWindow.gameObject);

                return mpLobbyWindow;
            }
            catch (Exception e)
            {
                Mod.LogException("MultiplayerLobby create", e);
                
                ErrorWindow.Show(e.Message);
                
                throw e;
            }
        }
        
        private void OnEnable()
        {
            // TODO - reset state
            var mainMenuManager = GameObject.Find("MainMenuManager").GetComponent<MainMenuManager>();
            mainMenuManager.InitializeCharacter();
            SoundEffectManager.Instance.PlayOneShot("se_in");
        }

        private void OnDisable()
        {
            Destroy(this);
        }

        void Awake()
        {
            lobbyMemberTemplate = transform.Find("Base/Lobby/Scroll View/Viewport/Content/LobbyMember").gameObject;
            lobbyMemberTemplate.SetActive(false);

            clubSelect = transform.Find("Club").gameObject;

            var selfEntryObj = Instantiate(lobbyMemberTemplate, lobbyMemberTemplate.transform.parent);
            selfEntry = selfEntryObj.AddComponent<MultiplayerPlayerSlot>();
            selfEntry.isSelf = true;
            selfEntryObj.SetActive(true);

            selfEntry.OnChangeClub += OnChangeClub;

            stubInitWindow = GetComponent<GameInitializeWindow>();

            var clubSelectWindowRoot = transform.Find("Club");
            foreach (var button in clubSelectWindowRoot.GetComponentsInChildren<Button>(true))
            {
                button.onClick.AddListener(() =>
                {
                    try
                    {
                        selfEntry.SetSelectedClub(button.name);
                    }
                    catch (Exception e)
                    {
                        Mod.LogException("SetSelectedClub", e);
                    }
                });
            }
        }
        
        public void UpdateMySlotIcons()
        {
            try
            {
                selfEntry._selectedCharacter = MainSceneManager.Instance.SelectedCharacter.ToList();
                Mod.logger.Log("got selected character list");
                
                selfEntry.UpdateSlotIcons(stubInitWindow);
            }
            catch (Exception e)
            {
                Mod.LogException("UpdateMySlotIcons", e);
            }
        }

        void OnChangeClub()
        {
            SoundEffectManager.Instance.PlayOneShot("se_ok");
            clubSelect.SetActive(true);
        }
    }

    class MultiplayerPlayerSlot : MonoBehaviour
    {
        private readonly FieldInfo f_selectedCharacter =
            AccessTools.Field(typeof(MainSceneManager), "_selectedCharacter");
        private readonly FieldInfo f_slotList 
            = AccessTools.Field(typeof(GameInitializeWindow), "slotList");
        internal List<string> _selectedCharacter = new List<string>();
        internal Button clubSelectButton;
        internal List<GameObject> slots;

        internal bool isSelf;

        internal delegate void OnChangeClub_delegate();

        internal OnChangeClub_delegate OnChangeClub = () => { };

        void Awake()
        {
            _selectedCharacter.Add("__random__");
            _selectedCharacter.Add("__random__");
            _selectedCharacter.Add("__random__");
            _selectedCharacter.Add("__random__");
            _selectedCharacter.Add("__random__");

            clubSelectButton = transform.Find("Content/ClubSelect").GetComponent<Button>();
            clubSelectButton.interactable = isSelf;
            if (isSelf)
            {
                clubSelectButton.onClick.AddListener(() => OnChangeClub());
            }

            slots = new List<GameObject>();

            var sampleLevelButton = GameObject.Find("EventSystem").transform.parent
                .Find("GameInitializeWindow/Base/Member/Slots/Slot/Level/Text_Lv")
                .GetComponent<TextMeshProUGUI>();
            foreach (Transform t in transform.Find("Content/CharaSlots"))
            {
                slots.Add(t.gameObject);

                var lvText = t.Find("Level/Text_Lv").GetComponent<TextMeshProUGUI>();
                lvText.font = sampleLevelButton.font;
                lvText.fontSharedMaterial = sampleLevelButton.fontSharedMaterial;
            }
        }

        public void SetSelectedClub(string clubName)
        {
            var child = clubSelectButton.transform.Find(clubName);

            if (child == null)
            {
                Mod.logger.Warning($"[MultiplayerPlayerSlot] Club {clubName} not found");
                return;
            }

            foreach (Transform t in clubSelectButton.transform)
            {
                t.gameObject.SetActive(false);
            }
            child.gameObject.SetActive(true);
            
            Mod.logger.Log($"Changed active club: {child}");
        }

        public void UpdateSlotIcons(GameInitializeWindow stubInitWindow)
        {
            Mod.logger.Log($"stubInitWindow={stubInitWindow} MSM.Instance={MainSceneManager.Instance} f_sc={f_selectedCharacter} f_slotList={f_slotList}");
            var oldSelected = (List<string>) f_selectedCharacter.GetValue(MainSceneManager.Instance);
            try
            {
                f_selectedCharacter.SetValue(MainSceneManager.Instance, _selectedCharacter);
                f_slotList.SetValue(stubInitWindow, slots);
                GameInitializeWindow_UpdateSlotIcon.noIntercept = true;
                
                stubInitWindow.UpdateSlotIcon();
            }
            finally
            {
                f_selectedCharacter.SetValue(MainSceneManager.Instance, oldSelected);
                GameInitializeWindow_UpdateSlotIcon.noIntercept = false;
            }
        }
    }
}