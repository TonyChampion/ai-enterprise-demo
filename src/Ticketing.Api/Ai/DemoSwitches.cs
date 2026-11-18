namespace Ticketing.Api.Ai;

/// <summary>How the app reaches the model. Demo 1 walks left to right.</summary>
public enum ModelRoute { Key, Identity, Gateway }

/// <summary>Demo 2: instructions and data in one string, or rules separate and data delimited.</summary>
public enum PromptMode { Naive, Hardened }

/// <summary>Demo 3: the model picks the customer, or the token does.</summary>
public enum ChatToolsMode { Safe, Naive }

/// <summary>
/// Stage switches, flipped from the demo page instead of checking out git tags live.
/// Every stage is also a tag in the repo, for anyone reading the code afterwards.
/// </summary>
public class DemoSwitches
{
    public ModelRoute Route { get; set; } = ModelRoute.Key;
    public PromptMode Prompt { get; set; } = PromptMode.Naive;
    public ChatToolsMode ChatTools { get; set; } = ChatToolsMode.Safe;
}
