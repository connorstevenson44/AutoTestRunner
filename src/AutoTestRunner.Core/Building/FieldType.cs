namespace AutoTestRunner.Building;

/// <summary>What a field in a step's or check's form holds, and so how it is shown and written.</summary>
public enum FieldType
{
    /// <summary>A few words, with ${variables} in them.</summary>
    Text,

    /// <summary>Text that can run to more than one line, such as a file's contents.</summary>
    LongText,

    /// <summary>Keys to press, such as Ctrl+S, which can be recorded by pressing them.</summary>
    Keys,

    /// <summary>A number, or a ${variable} that holds one.</summary>
    Number,

    /// <summary>Yes or no.</summary>
    YesNo,

    /// <summary>One of a few words.</summary>
    Choice,

    /// <summary>A control in the app: a name in elements.json, or how to find it. It can be picked in the app.</summary>
    Target,

    /// <summary>A path to a file, which can be browsed to.</summary>
    File,

    /// <summary>A path to a folder, which can be browsed to.</summary>
    Folder,

    /// <summary>A list of texts, one to a line.</summary>
    Lines,

    /// <summary>Anything else, as JSON: a macro's parameters, the variables a set step sets.</summary>
    Json,
}
