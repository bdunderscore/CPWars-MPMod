using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CPMod_Multiplayer.HarmonyPatches;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CPMod_Multiplayer.LobbyManagement
{
    public class MultiplayerLobbyWindow : MonoBehaviour
    {
        public static MultiplayerLobbyWindow Instance;
        
        private GameObject lobbyMemberTemplate;
        private MultiplayerPlayerSlot selfEntry;
        private GameInitializeWindow stubInitWindow;
        private GameObject clubSelect;

        private Lobby _lobby;

        private Dictionary<LobbyMember, MultiplayerPlayerSlot> _players = new Dictionary<LobbyMember, MultiplayerPlayerSlot>();

        internal static MultiplayerLobbyWindow Create()
        {
            try
            {
                var root = GameObject.Find("EventSystem").transform.parent;

                var prefabAsset = Mod.assetBundle.LoadAsset<GameObject>("MultiplayerLobbyWindow");
                var mpLobbyWindow = Instantiate(prefabAsset, root, false).AddComponent<MultiplayerLobbyWindow>();
                
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
            Instance = null;
        }

        void Awake()
        {
            Instance = this;
            try
            {
                _lobby = LobbyManager.CurrentLobby;
                
                transform.Find("Base/LobbyNumber").GetComponent<TextMeshProUGUI>().text =
                    _lobby.LobbyAddress;
                
                WindowHelpers.SetCloseButton(gameObject).onClick.AddListener(() =>
                {
                    LobbyManager.CurrentLobby = null;
                });
                
                transform.Find("Base/Button_Start").GetComponent<Button>().onClick.AddListener(() =>
                {
                    _lobby.StartGame();
                });
                
                var lobbyMemberTemplateTransform = transform.Find("Base/Lobby/Scroll View/Viewport/Content/LobbyMember");
                lobbyMemberTemplate = lobbyMemberTemplateTransform.gameObject;
                lobbyMemberTemplate.SetActive(false);

                var clubSelectTransform = transform.Find("Club");
                clubSelect = clubSelectTransform.gameObject;

                stubInitWindow = GetComponent<GameInitializeWindow>();

                var clubSelectWindowRoot = clubSelectTransform;
                foreach (var button in clubSelectWindowRoot.GetComponentsInChildren<Button>(true))
                {
                    button.onClick.AddListener(() =>
                    {
                        try
                        {
                            selfEntry.OnSetClub(button.name);
                        }
                        catch (Exception e)
                        {
                            Mod.LogException("SetSelectedClub", e);
                        }
                    });
                }
            }
            catch (Exception e)
            {
                Mod.LogException("[LobbyWindow] Awake", e);
            }
        }

        private void Start()
        {
            try
            {
                _lobby.Members.OnJoin += OnMemberJoin;
                _lobby.Members.OnPart += OnMemberPart;

                foreach (var member in _lobby.Members)
                {
                    OnMemberJoin(member);
                }
            }
            catch (Exception e)
            {
                Mod.logger.LogException("[LobbyWindow] Start", e);
            }
        }

        private void OnDestroy()
        {
            _lobby.Members.OnJoin -= OnMemberJoin;
            _lobby.Members.OnPart -= OnMemberPart;
        }

        void OnMemberJoin(LobbyMember member)
        {
            MultiplayerPlayerSlot slot = CreateMemberUI(member);
            _players[member] = slot;
            if (member == _lobby.Members.Self)
            {
                selfEntry = slot;
                slot.isSelf = true;
                selfEntry.OnChangeClub += OnChangeClub;
            }
        }

        void OnMemberPart(LobbyMember member)
        {
            if (_players.TryGetValue(member, out var slot))
            {
                _players.Remove(member);
                Destroy(slot.gameObject);
            }
        }
        
        void Update()
        {
            // keep start running
        }
        
        MultiplayerPlayerSlot CreateMemberUI(LobbyMember member)
        {
            var obj = Instantiate(lobbyMemberTemplate, lobbyMemberTemplate.transform.parent);
            var slot = obj.AddComponent<MultiplayerPlayerSlot>();
            slot.Member = member;
            slot.stubInitWindow = stubInitWindow;
            obj.SetActive(true);

            return slot;
        }
        
        public void UpdateMySlotIcons()
        {
            try
            {
                var self = _lobby.Members.Self;
                if (self != null)
                {
                    self.MemberState.characters = MainSceneManager.Instance.SelectedCharacter.ToArray();
                    self.RaiseOnChange();
                }
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
        internal LobbyMember Member;
        internal GameInitializeWindow stubInitWindow;
        
        private readonly FieldInfo f_selectedCharacter =
            AccessTools.Field(typeof(MainSceneManager), "_selectedCharacter");
        private readonly FieldInfo f_slotList 
            = AccessTools.Field(typeof(GameInitializeWindow), "slotList");
        internal List<string> _selectedCharacter = new List<string>();
        internal Button clubSelectButton;
        internal List<GameObject> slots;
        internal TextMeshProUGUI txtPlayerName;

        internal bool isSelf;

        internal delegate void OnChangeClub_delegate();

        internal OnChangeClub_delegate OnChangeClub = () => { };

        void Awake()
        {
            try
            {
                txtPlayerName = transform.Find("Content/PlayerName").GetComponent<TextMeshProUGUI>();

                clubSelectButton = transform.Find("Content/ClubSelect").GetComponent<Button>();
                
                var sampleLevelButton = GameObject.Find("EventSystem").transform.parent
                    .Find("GameInitializeWindow/Base/Member/Slots/Slot/Level/Text_Lv")
                    .GetComponent<TextMeshProUGUI>();

                slots = new List<GameObject>();
                foreach (Transform t in transform.Find("Content/CharaSlots"))
                {
                    slots.Add(t.gameObject);

                    var lvText = t.Find("Level/Text_Lv").GetComponent<TextMeshProUGUI>();
                    lvText.font = sampleLevelButton.font;
                    lvText.fontSharedMaterial = sampleLevelButton.fontSharedMaterial;
                }
            }
            catch (Exception e)
            {
                Mod.LogException("[MultiplayerPlayerSlot] Awake", e);
            }

        }
        
        void Start() {
            try
            {
                clubSelectButton.interactable = isSelf;
                if (isSelf)
                {
                    clubSelectButton.onClick.AddListener(() => OnChangeClub());
                }
                
                Member.OnChange += OnMemberChange;

                OnMemberChange(Member);
            }
            catch (Exception e)
            {
                Mod.LogException("[LobbyPlayerSlot] Start", e);
            }
        }

        private void OnDestroy()
        {
            Member.OnChange -= OnMemberChange;
        }

        void Update()
        {
            // Just make sure Start is called...
        }

        internal void OnMemberChange(LobbyMember _) {
            _selectedCharacter = new List<string>(Member.MemberState.characters);
            txtPlayerName.text = Member.DisplayName;
            SetClubDisplay(Member.MemberState.selectedClub);
            UpdateSlotIcons();
        }

        public void OnSetClub(string clubName)
        {
            Member.MemberState.selectedClub = clubName;
            Member.RaiseOnChange();
        }
        
        public void SetClubDisplay(string clubName) {
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
        }

        public void UpdateSlotIcons()
        {
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