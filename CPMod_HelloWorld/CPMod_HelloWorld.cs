using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityModManagerNet;

namespace CPMod_HelloWorld
{
    public class ModEntrypoint
    {
        static internal AssetBundle assetBundle;
        static internal UnityModManager.ModEntry modEntry;

        static bool Load(UnityModManager.ModEntry modEntry)
        {
            assetBundle = AssetBundle.LoadFromFile(Path.Combine(modEntry.Path, "CPMod_saveload.assetbundle"));
            ModEntrypoint.modEntry = modEntry;

            if (assetBundle == null)
            {
                modEntry.Logger.Error("Failed to load asset bundle");
                return false;
            }

            var checker = new GameObject("CPMod_SaveLoad_CheckForGameStart");
            checker.AddComponent<CheckForGameStart>();
            UnityEngine.Object.DontDestroyOnLoad(checker);
            modEntry.Logger.Log("Loaded successfully");

            return true;
        }
    }

    public class CheckForGameStart : MonoBehaviour
    {
        GameObject modPrefab;
        GameObject modInstance;

        void Awake()
        {
            modPrefab = ModEntrypoint.assetBundle.LoadAsset<GameObject>("MOD_SaveLoad");
            ModEntrypoint.modEntry.Logger.Log("CheckForGameStart: Awake");
        }

        void Update()
        {
            if (modInstance != null) return;

            var scene = SceneManager.GetSceneByName("GameScene");
            if (scene == null || !scene.IsValid()) return;

            ModEntrypoint.modEntry.Logger.Log("CheckForGameStart: Create prefab");

            var activeScene = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            modInstance = GameObject.Instantiate(modPrefab);
            SceneManager.SetActiveScene(activeScene);

            var parent = scene.GetRootGameObjects().Where(o => o.transform.Find("EventSystem") != null).First();
            modInstance.transform.SetParent(parent.transform, false);

            modInstance.AddComponent<SaveLoadManager>();

            ModEntrypoint.modEntry.Logger.Log("Created prefab with parent " + parent.name);
        }
    }


    struct RoomSaveData
    {
        internal Room room;
        internal int dominationTeam;
        internal float dominance;
        internal int training_power;
    }

#pragma warning disable 0649
    class UnitInfo
    {
        [SerializeField]
        internal string _name;
        [SerializeField]
        internal string _name_display;
        [SerializeField]
        internal int _energy;
        internal Character _character_data;
        [SerializeField]
        internal Room _inRoom;
        internal Room _lastRoom;
        [SerializeField]
        internal int _team;
        [SerializeField]
        internal bool _isShowDebugLog;
        [SerializeField]
        internal float _actionProc;
        internal string _actionName = "";
        internal string _actionNameDisplay = "";
        internal string _command = "";
        internal float _actionCost = 1f;
        [SerializeField]
        internal Room _targetRoom;
        [SerializeField]
        internal int _targetWayId;
        internal WayPoint _way;
        internal bool _isLeader;
        internal bool AutoPlay;
    }
#pragma warning restore 0649

    class SavedCharacter
    {
        int effort;
        float exp;
        int lv;
        int energy_max;
        int energy;
        int power;
        int speed;
        int intelligence;

        Character origin;

        internal void Apply()
        {
            origin.effort = effort;
            origin.exp = exp;
            origin.lv = lv;
            origin.energy_max = energy_max;
            origin.energy = energy;
            origin.power = power;
            origin.speed = speed;
            origin.intelligence = intelligence;
        }

        internal SavedCharacter(Character c)
        {
            this.origin = c;

            this.effort = c.effort;
            this.exp = c.exp;
            this.lv = c.lv;
            this.energy_max = c.energy_max;
            this.energy = c.energy;
            this.power = c.power;
            this.speed = c.speed;
            this.intelligence = c.intelligence;
        }
    }

    public class SaveLoadManager : MonoBehaviour {
        GameObject world;
        GameObject unitsRoot;
        Room[] rooms;

        RoomSaveData[] savedRooms;
        List<UnitInfo> savedUnits;
        GameManager gameManager;

        List<SavedCharacter> savedCharacters;

        int money;
        int hour;
        float minute;
        int day;
        float tickCount;
        float enemyPower;

        FieldInfo f_hour, f_minute, f_day, f_tickCount;

        FieldInfo f_uiObject;

        void Start() {
            ModEntrypoint.modEntry.Logger.Log("[SaveLoadManager] Start");

            try
            {
                f_uiObject = typeof(Room).GetField("_uiObject", BindingFlags.Instance | BindingFlags.NonPublic);
                f_hour = typeof(GameManager).GetField("_hour", BindingFlags.Instance | BindingFlags.NonPublic);
                f_minute = typeof(GameManager).GetField("_min", BindingFlags.Instance | BindingFlags.NonPublic);
                f_day = typeof(GameManager).GetField("_day", BindingFlags.Instance | BindingFlags.NonPublic);
                f_tickCount = typeof(GameManager).GetField("_tickCount", BindingFlags.Instance | BindingFlags.NonPublic);

                world = gameObject.scene.GetRootGameObjects().Where(o => o.name == "World").First();
                ModEntrypoint.modEntry.Logger.Log("[SaveLoadManager] World: " + world.name);
                rooms = world.transform.Find("Rooms").GetComponentsInChildren<Room>();
                ModEntrypoint.modEntry.Logger.Log("[SaveLoadManager] Rooms: " + rooms.Length);
                unitsRoot = world.transform.Find("Units").gameObject;
                ModEntrypoint.modEntry.Logger.Log("[SaveLoadManager] Units: " + unitsRoot.name);
                gameManager = gameObject.scene.GetRootGameObjects().Where(o => o.name == "GameManager").First()
                    .GetComponent<GameManager>();
                ModEntrypoint.modEntry.Logger.Log("[SaveLoadManager] GameManager: " + gameManager.gameObject.name);

                transform.Find("Button_save").GetComponent<Button>().onClick.AddListener(OnSave);
                transform.Find("Button_load").GetComponent<Button>().onClick.AddListener(OnLoad);
            } catch (Exception e)
            {
                ModEntrypoint.modEntry.Logger.LogException("SaveLoadManager: Start", e);
            }
        }

        void Update() { }

        void CopyUnitProps(object src, object dst)
        {
            foreach (var field in typeof(UnitInfo).GetFields(BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var srcField = src.GetType().GetField(field.Name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                var dstField = dst.GetType().GetField(field.Name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                dstField.SetValue(dst, srcField.GetValue(src));
            }
        }

        void OnSave()
        {
            try
            {
                ModEntrypoint.modEntry.Logger.Log("Saving...");

                money = gameManager.Money;
                hour = (int)f_hour.GetValue(gameManager);
                day = (int)f_day.GetValue(gameManager);
                minute = (float)f_minute.GetValue(gameManager);
                tickCount = (float)f_tickCount.GetValue(gameManager);
                enemyPower = gameManager.Enemy_power;

                savedRooms = new RoomSaveData[rooms.Length];

                for (int i = 0; i < rooms.Length; i++)
                {
                    savedRooms[i].room = rooms[i];
                    savedRooms[i].dominationTeam = rooms[i].DominationTeam;
                    savedRooms[i].dominance = rooms[i].Dominance;
                    savedRooms[i].training_power = rooms[i].TrainingPower;
                }

                savedUnits = new List<UnitInfo>();

                foreach (Unit unit in unitsRoot.GetComponentsInChildren<Unit>(false))
                {
                    ModEntrypoint.modEntry.Logger.Log("[Save] Found unit " + unit.Name);

                    var save = new UnitInfo();
                    CopyUnitProps(unit, save);

                    savedUnits.Add(save);
                }

                savedCharacters = CharacterManager.Instance.CharacterList.Values
                    .Select(c => new SavedCharacter(c)).ToList();

                ModEntrypoint.modEntry.Logger.Log("Done saving");
            } catch (Exception e)
            {
                ModEntrypoint.modEntry.Logger.LogException("Save error", e);
                ModEntrypoint.modEntry.Logger.Log(e.StackTrace);
            }
        }

        void OnLoad()
        {
            try
            {
                if (savedRooms == null)
                {
                    ModEntrypoint.modEntry.Logger.Error("No saved data");
                }

                ModEntrypoint.modEntry.Logger.Log("Loading...");

                gameManager.Money = money;
                f_hour.SetValue(gameManager, hour);
                f_minute.SetValue(gameManager, minute);
                f_day.SetValue(gameManager, day);
                f_tickCount.SetValue(gameManager, tickCount);
                gameManager.Enemy_power = enemyPower;

                savedCharacters.ForEach(c => c.Apply());

                for (int i = 0; i < savedRooms.Length; i++)
                {
                    var room = savedRooms[i].room;
                    room.DominationTeam = savedRooms[i].dominationTeam;
                    room.Dominance = savedRooms[i].dominance;
                    room.TrainingPower = savedRooms[i].training_power;
                }

                // Destroy any units not in the saved set
                HashSet<string> knownUnits = new HashSet<string>();
                foreach (var saved in savedUnits)
                {
                    ModEntrypoint.modEntry.Logger.Log("[Load] Preserving unit " + saved._name);
                    knownUnits.Add(saved._name);
                }

                Dictionary<string, Unit> preserved = new Dictionary<string, Unit>();
                List<Unit> toDestroy = new List<Unit>();
                foreach (var unit in unitsRoot.GetComponentsInChildren<Unit>())
                {
                    if (knownUnits.Contains(unit.name))
                    {
                        ModEntrypoint.modEntry.Logger.Log("[Load] Found unit " + unit.Name);
                        preserved.Add(unit.name, unit);
                        continue;
                    }

                    toDestroy.Add(unit);
                }

                foreach (var unit in toDestroy)
                {
                    ModEntrypoint.modEntry.Logger.Log("[Load] Destroy unit " + unit.Name);
                    gameManager.DeleteCharacter(unit.Name);
                    UnityEngine.Object.Destroy(unit.UIObject.gameObject);
                    UnityEngine.Object.Destroy(unit.gameObject);
                }

                // Clear room positions
                foreach (Room room in rooms)
                {
                    room.Units.Clear();
                }

                // Copy and/or create unit data
                var newUnits = new List<Unit>();
                foreach (var saved in savedUnits)
                {
                    Unit unit = null;
                    if (!preserved.TryGetValue(saved._name, out unit))
                    {
                        ModEntrypoint.modEntry.Logger.Log("[Load] Recreate unit " + saved._name);
                        unit = gameManager.PopCharacter(saved._name, saved._team, null, false);
                    }

                    CopyUnitProps(saved, unit);
                    unit.InRoom?.Insert(unit);
                    newUnits.Add(unit);
                }

                foreach (var unit in newUnits)
                {
                    unit.UpdatePosition();
                }

                gameManager.CalcAddMoney();

                // TODO - character levels
                // TODO - reset UI position for characters after rollback
                // TODO - money and timer
                // TODO - waypoint status rollback?

                ModEntrypoint.modEntry.Logger.Log("Done loading");
            } catch (Exception e)
            {
                ModEntrypoint.modEntry.Logger.LogException("Load error", e);
            }
        }
    }
}