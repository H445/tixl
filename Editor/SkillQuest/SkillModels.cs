using Newtonsoft.Json;

namespace T3.Editor.SkillQuest;

// Maybe useful for later structuring
// public sealed class QuestZone
// {
//     public string Title = string.Empty;
//     public List<QuestTopic> Topics = [];
//     
//     
//     public static List<QuestZone> CreateZones()
//     {
//
//
// }

public struct QuestTopicLink
{
    public Guid SourceTopicId;
    public Guid TargetTopicId;
    
    [JsonIgnore]
    public QuestTopic SourceTopic;
    
    [JsonIgnore]
    public QuestTopic TargetTopic;

    [JsonIgnore]
    public bool Unlocked;
}

public sealed class QuestTopic
{
    public Guid Id = Guid.Empty;
    public string Title= string.Empty;
    public List<QuestLevel> Levels = [];
    public List<Guid> PathsFromId=[];
    public Requirements Requirement = Requirements.None;
    
    [JsonIgnore]
    public List<LevelResult>  UserResults=[];

    public enum Requirements
    {
        None,
        IsValidStartPoint,
        AnyInputPath,
        AllInputPaths,
    }
        
    public static List<QuestTopic> CreateAllLevels()
    {
        return
            [
                new QuestTopic
                    {
                        Title = "Welcome",
                        Id = new Guid("D5E76A36-DEB8-42D8-A1BB-6B85B7848662"),
                        Levels =
                            [
                                new QuestLevel
                                    {
                                        Title = "Let's get started",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                                new QuestLevel
                                    {
                                        Title = "Move it!",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                                new QuestLevel
                                    {
                                        Title = "It's there after all",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                            ]
                    },
                new QuestTopic
                    {
                        Title = "You can change it",
                        Id = new Guid("AE01DCC2-1382-4771-B6E4-51ED915D610E"),
                        Levels =
                            [
                                new QuestLevel
                                    {
                                        Title = "Let's get started",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                                new QuestLevel
                                    {
                                        Title = "Move it!",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                                new QuestLevel
                                    {
                                        Title = "It's there after all",
                                        SymbolId = new Guid("2881CE06-039F-4515-A672-5576CF78E808")
                                    },
                            ]
                    },                
                
            ];
    }
}

public sealed class QuestLevel
{
    public string Title = string.Empty;
    public Guid SymbolId = Guid.Empty;
}

public sealed class UserProgress
{
    public QuestTopic ActiveTopicId;
    public List<LevelResult> Results =[];
}

public sealed class LevelResult
{
    public DateTime StartTime;
    public DateTime EndTime;
    public Results Result;
    public int Rating;
    
    public enum Results {
        Started,
        Skipped,
        Completed,
    }
}


