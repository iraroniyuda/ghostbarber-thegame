using UnityEngine;
using System.IO;
using System.Collections.Generic;
#if UNITY_ANALYTICS
using UnityEngine.Analytics;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

public struct HighscoreEntry : System.IComparable<HighscoreEntry>
{
    public string name;
    public int score;

    public int CompareTo(HighscoreEntry other)
    {
        // We want to sort from highest to lowest, so inverse the comparison.
        return other.score.CompareTo(score);
    }
}

public class PlayerData
{
    static protected PlayerData m_Instance;
    static public PlayerData instance { get { return m_Instance; } }

    protected string saveFile = "";

    public int coins;
    public int premium;
    public Dictionary<Consumable.ConsumableType, int> consumables = new Dictionary<Consumable.ConsumableType, int>();   // Inventory of owned consumables and quantity.

    public List<string> characters = new List<string>();    // Inventory of characters owned.
    public int usedCharacter;                               // Currently equipped character.
    public int usedAccessory = -1;
    public List<string> characterAccessories = new List<string>();  // List of owned accessories, in the form "charName:accessoryName".
    public List<string> themes = new List<string>();                // Owned themes.
    public int usedTheme;                                           // Currently used theme.
    public List<HighscoreEntry> highscores = new List<HighscoreEntry>();
    public List<MissionBase> missions = new List<MissionBase>();

    public string previousName = "Mr. Drac";  // Set default name to "Mr. Drac"

    public bool licenceAccepted;
    public bool tutorialDone;

    public float masterVolume = float.MinValue, musicVolume = float.MinValue, masterSFXVolume = float.MinValue;

    // First Time User Experience tracking
    public int ftueLevel = 0;
    public int rank = 0;

    // Save file versioning for backwards compatibility
    static int s_Version = 12;

    public void Consume(Consumable.ConsumableType type)
    {
        if (!consumables.ContainsKey(type))
            return;

        consumables[type] -= 1;
        if (consumables[type] == 0)
        {
            consumables.Remove(type);
        }

        Save();
    }

    public void Add(Consumable.ConsumableType type)
    {
        if (!consumables.ContainsKey(type))
        {
            consumables[type] = 0;
        }

        consumables[type] += 1;
        Save();
    }

    public void AddCharacter(string name)
    {
        characters.Add(name);
    }

    public void AddTheme(string theme)
    {
        themes.Add(theme);
    }

    public void AddAccessory(string name)
    {
        characterAccessories.Add(name);
    }

    // Mission management
    public void CheckMissionsCount()
    {
        while (missions.Count < 2)
            AddMission();
    }

    public void AddMission()
    {
        int val = Random.Range(0, (int)MissionBase.MissionType.MAX);
        MissionBase newMission = MissionBase.GetNewMissionFromType((MissionBase.MissionType)val);
        newMission.Created();
        missions.Add(newMission);
    }

    public void StartRunMissions(TrackManager manager)
    {
        for (int i = 0; i < missions.Count; ++i)
        {
            missions[i].RunStart(manager);
        }
    }

    public void UpdateMissions(TrackManager manager)
    {
        for (int i = 0; i < missions.Count; ++i)
        {
            missions[i].Update(manager);
        }
    }

    public bool AnyMissionComplete()
    {
        for (int i = 0; i < missions.Count; ++i)
        {
            if (missions[i].isComplete) return true;
        }

        return false;
    }

    public void ClaimMission(MissionBase mission)
    {
        premium += mission.reward;
#if UNITY_ANALYTICS
        AnalyticsEvent.ItemAcquired(
            AcquisitionType.Premium,
            "mission",
            mission.reward,
            "fishbones",
            premium,
            "consumable",
            rank.ToString()
        );
#endif
        missions.Remove(mission);
        CheckMissionsCount();
        Save();
    }

    // High Score management
    public int GetScorePlace(int score)
    {
        HighscoreEntry entry = new HighscoreEntry { score = score, name = "" };
        int index = highscores.BinarySearch(entry);
        return index < 0 ? ~index : index;
    }

    public void InsertScore(int score, string name)
    {
        HighscoreEntry entry = new HighscoreEntry { score = score, name = name };
        highscores.Insert(GetScorePlace(score), entry);

        // Keep only the top 10 scores
        while (highscores.Count > 10)
            highscores.RemoveAt(highscores.Count - 1);
    }

    // File management
    static public void Create()
    {
        if (m_Instance == null)
        {
            m_Instance = new PlayerData();
            CoroutineHandler.StartStaticCoroutine(CharacterDatabase.LoadDatabase());
            CoroutineHandler.StartStaticCoroutine(ThemeDatabase.LoadDatabase());
        }

        m_Instance.saveFile = Application.persistentDataPath + "/save.bin";

        if (File.Exists(m_Instance.saveFile))
        {
            m_Instance.Read();
        }
        else
        {
            NewSave();
        }

        m_Instance.CheckMissionsCount();
    }

    static public void NewSave()
    {
        m_Instance.characters.Clear();  // Ensure the list is clear before adding new characters.
        m_Instance.themes.Clear();
        m_Instance.missions.Clear();
        m_Instance.characterAccessories.Clear();
        m_Instance.consumables.Clear();

        m_Instance.usedCharacter = 0;
        m_Instance.usedTheme = 0;
        m_Instance.usedAccessory = -1;

        m_Instance.coins = 0;
        m_Instance.premium = 0;

        // Add default character "Mr. Drac"
        m_Instance.characters.Add("Mr. Drac");  // Make sure you add a default character here.

        // Add default theme
        m_Instance.themes.Add("Day");

        m_Instance.ftueLevel = 0;
        m_Instance.rank = 0;

        m_Instance.CheckMissionsCount();

        m_Instance.Save();  // Save the new game data to a file.
    }


    public void Read()
    {
        using (BinaryReader r = new BinaryReader(new FileStream(saveFile, FileMode.Open)))
        {
            int ver = r.ReadInt32();

            // Read coins and consumables
            coins = r.ReadInt32();
            consumables.Clear();
            int consumableCount = r.ReadInt32();
            for (int i = 0; i < consumableCount; ++i)
            {
                consumables.Add((Consumable.ConsumableType)r.ReadInt32(), r.ReadInt32());
            }

            // Read characters
            characters.Clear();
            int charCount = r.ReadInt32();
            for (int i = 0; i < charCount; ++i)
            {
                string charName = r.ReadString();
                characters.Add(charName);  // Add characters from the save file to the list.
            }

            // Ensure there's always a character in the list
            if (characters.Count == 0)
            {
                characters.Add("Mr. Drac");  // Fallback to default character if none exist in the save file.
            }

            usedCharacter = r.ReadInt32();



            if (usedCharacter >= characters.Count)
            {
                usedCharacter = 0;  // Fallback to the first character if invalid
            }

            characterAccessories.Clear();
            int accCount = r.ReadInt32();
            for (int i = 0; i < accCount; ++i)
            {
                characterAccessories.Add(r.ReadString());
            }

            themes.Clear();
            int themeCount = r.ReadInt32();
            for (int i = 0; i < themeCount; ++i)
            {
                themes.Add(r.ReadString());
            }

            usedTheme = r.ReadInt32();

            if (ver >= 2)
            {
                premium = r.ReadInt32();
            }

            if (ver >= 3)
            {
                highscores.Clear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; ++i)
                {
                    highscores.Add(new HighscoreEntry { name = r.ReadString(), score = r.ReadInt32() });
                }
            }

            if (ver >= 4)
            {
                missions.Clear();
                int count = r.ReadInt32();
                for (int i = 0; i < count; ++i)
                {
                    MissionBase tempMission = MissionBase.GetNewMissionFromType((MissionBase.MissionType)r.ReadInt32());
                    tempMission.Deserialize(r);
                    missions.Add(tempMission);
                }
            }

            if (ver >= 7)
            {
                previousName = r.ReadString();
            }

            if (ver >= 8)
            {
                licenceAccepted = r.ReadBoolean();
            }

            if (ver >= 9)
            {
                masterVolume = r.ReadSingle();
                musicVolume = r.ReadSingle();
                masterSFXVolume = r.ReadSingle();
            }

            if (ver >= 10)
            {
                ftueLevel = r.ReadInt32();
                rank = r.ReadInt32();
            }

            if (ver >= 12)
            {
                tutorialDone = r.ReadBoolean();
            }
        }
    }

    public void Save()
    {
        using (BinaryWriter w = new BinaryWriter(new FileStream(saveFile, FileMode.OpenOrCreate)))
        {
            w.Write(s_Version);
            w.Write(coins);

            w.Write(consumables.Count);
            foreach (var p in consumables)
            {
                w.Write((int)p.Key);
                w.Write(p.Value);
            }

            w.Write(characters.Count);
            foreach (string c in characters)
            {
                w.Write(c);
            }

            w.Write(usedCharacter);

            w.Write(characterAccessories.Count);
            foreach (string a in characterAccessories)
            {
                w.Write(a);
            }

            w.Write(themes.Count);
            foreach (string t in themes)
            {
                w.Write(t);
            }

            w.Write(usedTheme);
            w.Write(premium);

            w.Write(highscores.Count);
            foreach (var highscore in highscores)
            {
                w.Write(highscore.name);
                w.Write(highscore.score);
            }

            w.Write(missions.Count);
            foreach (var mission in missions)
            {
                w.Write((int)mission.GetMissionType());
                mission.Serialize(w);
            }

            w.Write(previousName);
            w.Write(licenceAccepted);

            w.Write(masterVolume);
            w.Write(musicVolume);
            w.Write(masterSFXVolume);

            w.Write(ftueLevel);
            w.Write(rank);

            w.Write(tutorialDone);
        }
    }
}

// Helper class for testing in the editor
#if UNITY_EDITOR
public class PlayerDataEditor : Editor
{
    [MenuItem("Trash Dash Debug/Clear Save")]
    static public void ClearSave()
    {
        File.Delete(Application.persistentDataPath + "/save.bin");
    }

    [MenuItem("Trash Dash Debug/Give 1000000 fishbones and 1000 premium")]
    static public void GiveCoins()
    {
        PlayerData.instance.coins += 1000000;
        PlayerData.instance.premium += 1000;
        PlayerData.instance.Save();
    }

    [MenuItem("Trash Dash Debug/Give 10 Consumables of each type")]
    static public void AddConsumables()
    {
        for (int i = 0; i < ShopItemList.s_ConsumablesTypes.Length; ++i)
        {
            Consumable c = ConsumableDatabase.GetConsumbale(ShopItemList.s_ConsumablesTypes[i]);
            if (c != null)
            {
                PlayerData.instance.consumables[c.GetConsumableType()] = 10;
            }
        }

        PlayerData.instance.Save();
    }
}
#endif
