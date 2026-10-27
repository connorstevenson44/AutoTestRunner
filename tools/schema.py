# Writes the JSON schemas in schema/ from the vocabulary below, which must match Vocabulary.cs: SchemaTests fails when
# the two differ. Run it from anywhere: python3 tools/schema.py
import json, os
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "schema")
S = "http://json-schema.org/draft-07/schema#"
def text(d): return {"type": "string", "description": d}
seconds = {"type": ["number", "string"], "description": "How long to wait, in seconds, before it counts as stuck. An unattended run triples it."}
common = {
  "say": text("Your words for the step, shown when pointing at its row; on a wait, read after it, so it can use what was found. Alone, it is a line of its own, for what isn't a step."),
  "note": text("For whoever maintains the script. Not shown."),
  "as": text("Keeps what the step found or printed in a variable of this name, as ${name} and ${name.part}."),
  "what": text("What to call the control in a message, such as \"the start screen\"."),
  "within": seconds,
  "disabled": {"type": "boolean", "description": "Skips the step."},
}
target = {"$ref": "#/definitions/target"}
def step(kind, value, desc, required_extra=(), **extra):
    props = {kind: dict(value, description=desc) if isinstance(value, dict) and "$ref" not in value else {"allOf":[value], "description": desc}}
    props.update(extra)
    return {"type": "object", "required": [kind, *required_extra], "properties": {**props, **common}, "additionalProperties": False}
steps = {
 "say": {"type":"object","required":["say"],"properties":common,"additionalProperties":False},
 "startApp": step("startApp", {"type":["object","boolean"]}, "Closes any copy of the app, starts it, and places it beside the panel."),
 "closeApp": step("closeApp", {"type":["object","boolean"]}, "Closes the app."),
 "click": step("click", target, "Clicks a control: invokes, toggles or selects it, or with how, clicks it with the mouse.", how={"enum":["mouse","right","double"], "description":"mouse clicks with the mouse, for controls UI Automation can't invoke; right right-clicks; double double-clicks."}, whileEnabled={"type":"boolean","description":"Clicks it again and again while it is there and can be used, such as Up until the top folder. Not being there is fine."}),
 "drag": step("drag", target, "Drags a control with the mouse and drops it on another.", ["to"], to={"$ref":"#/definitions/target","description":"The control to drop it on."}),
 "menu": step("menu", {"type":["array","string"],"items":{"type":"string"}}, "Picks from a menu: each item in turn, such as [\"File\", \"Export…\"]. From the menu bar, or with on, a control's right-click menu.", on={"$ref":"#/definitions/target","description":"Right-clicks this control first, and picks from the menu that opens."}),
 "type": step("type", target, "Types into a text box a key at a time, then Tab.", ["text"], text=text("What to type. Empty clears the box."), secret={"type":"boolean","description":"Kept out of the log, and not checked after typing."}),
 "choose": step("choose", target, "Picks an option from a drop-down.", ["option"], option=text("The option's text.")),
 "tick": step("tick", target, "Ticks or clears a tick box.", checked={"type":"boolean","description":"false clears it. Ticks it when left out."}),
 "setValue": step("setValue", target, "Sets a slider's or a number box's value, or a box's text in one go, such as a date picker's.", ["to"], to=text("The value, such as 150 or 2026-10-26.")),
 "expand": step("expand", {"type":["array","string"],"items":{"type":"string"}}, "Opens a tree item by its path, such as [\"Contoso\", \"Orders\"]: it opens each item on the way and selects the last.", **{"in": {"$ref":"#/definitions/target","description":"The tree, when the window has more than one."}}),
 "hover": step("hover", target, "Points the mouse at a control, such as a row whose buttons only show on hover."),
 "tab": step("tab", {"type":"string"}, "Opens a tab by its header."),
 "find": step("find", target, "Waits for a control to be on screen."),
 "waitGone": step("waitGone", target, "Waits for a control to go."),
 "waitEnabled": step("waitEnabled", target, "Waits for a control to be there and usable, not greyed out."),
 "useWindow": step("useWindow", {"type":"string"}, "Waits for another window of the app's by its title, and drives that one from now on, as after a sign-in window."),
 "waitText": step("waitText", target, "Waits for a control's text to read a certain way.", startsWith=text("Waits until it starts with this."), contains=text("Waits until it has this in it."), equals=text("Waits until it is exactly this."), matches=text("Waits until it matches this .NET regex."), changes={"type":"boolean","description":"Waits until it differs from when the step started."}, new={"type":"boolean","description":"Waits until it differs from the app's status before the last click (suite.json app.status)."}),
 "replaceText": step("replaceText", target, "Replaces the first match of a regex in a text box's value, in one go as a paste would.", ["regex"], regex=text("A .NET regex, multi-line."), replacement=text("What to put instead; $1 is the first group.")),
 "key": step("key", {"type":"string"}, "Presses a key or a combination, such as Tab, Enter or Ctrl+A."),
 "answerDialog": step("answerDialog", {"type":"string"}, "Waits for a Windows message box with this title and presses a button.", ["button"], button={"enum":["Yes","No","OK","Cancel"]}, expect=text("Part of the message; the case fails if it isn't there.")),
 "pickFile": step("pickFile", {"type":"string"}, "Picks a file in the Windows file picker with this title, by typing its path.", ["path"], path=text("The file to pick.")),
 "pickFolder": step("pickFolder", {"type":"string"}, "Picks a folder in the Windows folder picker with this title.", ["path"], path=text("The folder to pick.")),
 "call": step("call", {"type":"string"}, "Runs a macro from macros/.", **{"with": {"type":"object","additionalProperties":{"type":["string","number","boolean"]},"description":"The macro's parameters."}}),
 "repeat": step("repeat", {"type":["array","string","object"],"items":{"type":"string"},"properties":{"rows":text("A CSV file whose first line names its columns, or a JSON list of objects, in the suite's folder. Each row is ${item}, its columns ${item.Column}.")},"required":["rows"],"additionalProperties":False}, "Runs steps once for each item of a list, or each row of a data file.", ["steps"], steps={"$ref":"#/definitions/steps"}, item=text("The variable each item goes in. item when left out.")),
 "whileShows": step("whileShows", target, "Runs steps each time a control shows, such as a window asking about each step of a removal, until another control's text says it is done.", ["steps","untilText"], steps={"$ref":"#/definitions/steps"}, untilText={"$ref":"#/definitions/target","description":"The control whose text says it is done, such as a status bar."}, startsWith=text("Done when untilText starts with this."), contains=text("Done when untilText has this in it."), equals=text("Done when untilText is exactly this."), matches=text("Done when untilText matches this .NET regex."), new={"type":"boolean","description":"Only text that differs from the app's status before the last click counts."}),
 "if": step("if", {"$ref":"#/definitions/check"}, "Runs steps when a check passes, and else when it doesn't.", ["steps"], steps={"$ref":"#/definitions/steps"}, **{"else": {"$ref":"#/definitions/steps"}}),
 "set": step("set", {"type":"object","additionalProperties":{"type":["string","number","boolean"]}}, "Sets variables."),
 "run": step("run", {"type":"string"}, "Runs a program and keeps its exit code and output (with as: ${name.code}, ${name.output}, ${name.first}).", args={"type":"array","items":{"type":"string"}}, env={"type":"object","additionalProperties":{"type":"string"}}),
 "powershell": step("powershell", {"type":["string","array"],"items":{"type":"string"}}, "Runs a PowerShell script, which gets args as $args. ${...} in the script is PowerShell's, not the runner's.", args={"type":"array","items":{"type":"string"}}),
 "script": step("script", {"type":"string"}, "Runs a .ps1 or .cs file in the suite, relative to its folder. A .ps1 runs with Windows PowerShell and gets args as $args; a .cs runs with dotnet run, which needs the .NET 10 SDK, and gets them as args. It fails when the script exits with a code other than 0; with as, it keeps the code and output.", args={"type":"array","items":{"type":"string"}}),
 "wait": step("wait", {"type":["number","string"]}, "Waits this many seconds, for an app that shows nothing to wait for."),
 "copy": step("copy", {"type":"string"}, "Copies a file, or a folder and everything in it, replacing what is there.", ["to"], to=text("Where to: a file, a folder to copy a file into, or the folder to copy a folder as.")),
 "delete": step("delete", {"type":"string"}, "Deletes a file, or a folder and everything in it. Nothing being there is fine."),
 "writeFile": step("writeFile", {"type":"string"}, "Writes a file, making its folder if need be.", ["text"], text={"type":["string","array"],"items":{"type":"string"},"description":"What to write: a text, or a list of lines."}, append={"type":"boolean","description":"Adds to the end of the file instead of replacing it."}),
 "unzip": step("unzip", {"type":"string"}, "Unzips a .zip file into a folder, replacing the folder when it is there.", ["to"], to=text("The folder.")),
 "startService": step("startService", {"type":"string"}, "Starts a Windows service and waits until it runs."),
 "stopService": step("stopService", {"type":"string"}, "Stops a Windows service and waits until it has stopped."),
 "setJsonValue": step("setJsonValue", {"type":"string"}, "Sets a setting in a JSON file, such as appsettings.json. Comments in the file are dropped.", ["path","to"], path=text("The setting's path, such as Orders:Region."), to=text("The value. A number or true and false stays one when the old value was.")),
 "setAppSetting": step("setAppSetting", {"type":"string"}, "Sets an appSettings value in a .config file, adding it when it isn't there.", ["key","to"], key=text("The appSettings key."), to=text("The value.")),
 "query": step("query", {"type":"string"}, "Runs SQL on a database named in suite.json's databases. With as, keeps the first value it reads, its rows as ${name.rows} and all it read as ${name.output}.", ["sql"], sql=text("The SQL. Values go in params and are written in it by name, such as @id, or :id for Oracle."), params={"type":"object","additionalProperties":{"type":["string","number","boolean"]},"description":"Values for the SQL's parameters, by name."}),
 "send": step("send", {"type":"string"}, "Puts a message on a queue named in suite.json's queues.", message=text("The message's text."), file=text("A file in the suite whose text is the message.")),
 "receive": step("receive", {"type":"string"}, "Takes the next message off a queue named in suite.json's queues, waiting within seconds (10 when left out) for one. With as, keeps its text.", equals=text("The text the message should be."), contains=text("Text the message should have."), matches=text("A regex the message should match.")),
 "purge": step("purge", {"type":"string"}, "Takes every message off a queue named in suite.json's queues."),
 "checkpoint": step("checkpoint", {"type":"string"}, "Ends a case: checks the runner makes, things to look at, and buttons that open them.", checks={"type":"array","items":{"$ref":"#/definitions/check"}}, look={"type":"array","items":{"type":"string"}}, tools={"type":"array","items":{"$ref":"#/definitions/tool"}}),
}
def check(kind, desc, required_extra=(), **extra):
    return {"type":"object","required":[kind,*required_extra],"properties":{kind: text(desc), **extra, "label": text("What the check says instead of its own wording."), "within": seconds},"additionalProperties":False}
checks = {
 "fileExists": check("fileExists","A file or folder that should be there."),
 "fileMissing": check("fileMissing","A file or folder that shouldn't be there."),
 "service": check("service","A Windows service's name.", state=text("Running, Stopped, or absent (the default) for no such service.")),
 "scheduledTask": check("scheduledTask","A scheduled task's full path, such as \\\\Contoso\\\\Orders Export-main.", exists={"type":"boolean"}),
 "jsonValue": check("jsonValue","A JSON file.", ["path","equals"], path=text("The setting's path, such as Orders:Region."), equals=text("The value, or <absent>.")),
 "appSetting": check("appSetting","A .config file.", ["key","equals"], key=text("The appSettings key."), equals=text("The value, or <absent>.")),
 "connectionString": check("connectionString","A .config file.", ["name"], name=text("The connection string's name.")),
 "fileContains": check("fileContains","A file.", ["text"], text=text("Text it should have.")),
 "logged": check("logged","A log file.", ["text"], text=text("A line it should have.")),
 "fileCount": check("fileCount","A folder.", ["equals"], pattern=text("Such as *.pdf."), equals={"type":["integer","string"]}, folders={"type":"boolean","description":"Counts folders instead of files."}, recursive={"type":"boolean","description":"Counts inside the folders in it too."}),
 "fileVersion": check("fileVersion","An exe or dll.", ["startsWith"], startsWith=text("Such as 1.1.0.")),
 "output": check("output","A program to run.", ["contains"], args={"type":"array","items":{"type":"string"}}, contains=text("Text it should print.")),
 "lastLine": check("lastLine","A file being written, such as a heartbeat log.", ["contains"], contains=text("Text its last line should have.")),
 "equals": check("equals","A value, usually ${captured}.", ["to"], to=text("What it should be.")),
 "matches": check("matches","A value, usually ${captured}.", ["regex"], regex=text("A .NET regex it should match.")),
 "contains": check("contains","A value, usually ${captured}.", ["text"], text=text("Text it should have.")),
 "lacks": check("lacks","A value, usually ${captured}.", ["text"], text=text("Text it shouldn't have.")),
 "shows": {"type":"object","required":["shows"],"properties":{"shows":{"$ref":"#/definitions/target","description":"A control that should be in the app now."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "enabled": {"type":"object","required":["enabled"],"properties":{"enabled":{"$ref":"#/definitions/target","description":"A control that should be there and usable."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "disabled": {"type":"object","required":["disabled"],"properties":{"disabled":{"$ref":"#/definitions/target","description":"A control that should be greyed out."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "ticked": {"type":"object","required":["ticked"],"properties":{"ticked":{"$ref":"#/definitions/target","description":"A tick box that should be ticked."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "notTicked": {"type":"object","required":["notTicked"],"properties":{"notTicked":{"$ref":"#/definitions/target","description":"A tick box that shouldn't be ticked."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "selected": {"type":"object","required":["selected"],"properties":{"selected":{"$ref":"#/definitions/target","description":"An item, tab or option that should be the one picked."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "focused": {"type":"object","required":["focused"],"properties":{"focused":{"$ref":"#/definitions/target","description":"A control that should have the keyboard."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "says": {"type":"object","required":["says"],"properties":{"says":{"$ref":"#/definitions/target","description":"A control whose text, or what is typed in it, should read a certain way."},"equals":text("It is exactly this."),"startsWith":text("It starts with this."),"contains":text("It has this in it."),"matches":text("It matches this .NET regex."),"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "rows": {"type":"object","required":["rows","equals"],"properties":{"rows":{"$ref":"#/definitions/target","description":"A list, grid or tree."},"equals":{"type":["integer","string"],"description":"How many rows or items it should have."},"label":text("What the check says instead of its own wording."),"within":seconds},"additionalProperties":False},
 "nothingLeft": {"type":"object","required":["nothingLeft"],"properties":{"nothingLeft":{"type":"boolean","description":"Nothing the suite's leftovers script finds is left."},"label":text("What the check says."),"within":seconds},"additionalProperties":False},
}
checks.update({
 "fileMatches": check("fileMatches","A file.", ["regex"], regex=text("A .NET regex its text should match; ^ and $ match at each line.")),
 "registry": check("registry","A registry key, such as HKLM\\SOFTWARE\\Contoso.", ["value","equals"], value=text("The value's name."), equals=text("What it should be, or <absent>.")),
 "process": check("process","A program, by its name with or without .exe.", running={"type":"boolean","description":"false: it shouldn't be running."}),
 "eventLog": check("eventLog","A Windows event log, such as Application.", ["contains"], source=text("The source that wrote it."), contains=text("Text the entry should have. Entries since the case started count.")),
 "web": check("web","A web address, such as http://localhost:5000/health.", status={"type":["integer","string"],"description":"The HTTP status it should answer with. 200 when left out."}, contains=text("Text the page should have.")),
 "clipboard": check("clipboard","Text the clipboard should have."),
 "atMost": check("atMost","A number, usually ${kept}, such as ${deploy.seconds}.", ["to"], to=text("The most it may be.")),
 "atLeast": check("atLeast","A number, usually ${kept}.", ["to"], to=text("The least it may be.")),
 "dbValue": check("dbValue","A database named in suite.json's databases. With none of equals, contains, matches or rows, the query should read a row.", ["sql"], sql=text("The SQL that reads the value: the first column of the first row."), params={"type":"object","additionalProperties":{"type":["string","number","boolean"]},"description":"Values for the SQL's parameters, by name."}, equals=text("What the value should be. <null> for a database null."), contains=text("Text the value should have."), matches=text("A regex the value should match."), rows={"type":["integer","string"],"description":"How many rows it should read, or change."}),
 "queue": check("queue","A queue named in suite.json's queues. Looks without taking messages off it.", count={"type":["integer","string"],"description":"How many messages should be waiting."}, has=text("Text one of the waiting messages should have.")),
})
def tool(kind, desc, **extra):
    return {"type":"object","required":[kind],"properties":{kind: text(desc), "label": text("The button's text."), **extra},"additionalProperties":False}
tools = {
 "openFolder": tool("openFolder","Opens a folder in File Explorer."),
 "openFile": tool("openFile","Opens a file in Notepad."),
 "console": tool("console","A PowerShell script to run in a window that stays open.", args={"type":"array","items":{"type":"string"}}),
 "app": tool("app","A program or document to open, such as taskschd.msc.", args={"type":"array","items":{"type":"string"}}),
}
selector_props = {
 "element": text("A control named in elements.json."),
 "with": {"type":"object","additionalProperties":{"type":["string","number"]},"description":"Values for the element's ${parameters}."},
 "id": text("Its AutomationId, which WPF takes from x:Name when none is set. Inside a template it repeats on every row."),
 "name": text("Its name: a button's text, a box's label."),
 "type": {"enum":["AppBar","Button","Calendar","CheckBox","ComboBox","Custom","DataGrid","DataItem","Document","Edit","Group","Header","HeaderItem","Hyperlink","Image","List","ListItem","Menu","MenuBar","MenuItem","Pane","ProgressBar","RadioButton","ScrollBar","SemanticZoom","Separator","Slider","Spinner","SplitButton","StatusBar","Tab","TabItem","Table","Text","Thumb","TitleBar","ToolBar","ToolTip","Tree","TreeItem","Window"],"description":"Its UI Automation control type."},
 "helpText": text("Its tooltip."),
 "className": text("Its class name."),
 "match": {"enum":["exact","startsWith","contains"],"description":"How name and helpText are compared. exact when left out."},
 "within": {"$ref":"#/definitions/target","description":"Only looks inside this control, or with type Window, this window of the app's."},
 "sameRowAs": {"$ref":"#/definitions/target","description":"Only controls on the same row as this one, such as the buttons beside a file's name."},
 "scrollToFind": {"type":"boolean","description":"Scrolls the window's long lists until it is built, for lists that only build the rows in view."},
 "cell": {"type":"object","required":["row","column"],"properties":{"row":text("What one of the row's cells says."),"column":text("The column's header.")},"additionalProperties":False,"description":"A grid's cell, by its row and its column."},
}
for short, t in [("button","Button"),("text","Text"),("edit","Edit"),("tabItem","TabItem"),("comboBox","ComboBox"),("checkBox","CheckBox"),("radioButton","RadioButton"),("listItem","ListItem"),("list","List"),("window","Window"),("treeItem","TreeItem"),("menuItem","MenuItem"),("dataItem","DataItem"),("hyperlink","Hyperlink")]:
    selector_props[short] = text(f"Shorthand for type {t} with this name.")
defs = {
 "target": {"description":"A control: an element's name from elements.json, a selector, or a list of either to try in order.",
            "anyOf":[{"type":"string"},{"$ref":"#/definitions/selector"},{"type":"array","items":{"anyOf":[{"type":"string"},{"$ref":"#/definitions/selector"}]}}]},
 "selector": {"type":"object","properties":selector_props,"additionalProperties":False},
 "steps": {"type":"array","items":{"$ref":"#/definitions/step"}},
 "step": {"anyOf":[{"$ref":f"#/definitions/{k}Step"} for k in steps]},
 "check": {"anyOf":[{"$ref":f"#/definitions/{k}Check"} for k in checks]},
 "tool": {"anyOf":[{"$ref":f"#/definitions/{k}Tool"} for k in tools]},
}
for k,v in steps.items(): defs[k+"Step"]=v
for k,v in checks.items(): defs[k+"Check"]=v
for k,v in tools.items(): defs[k+"Tool"]=v
case = {"$schema":S,"$id":"case.schema.json","title":"AutoTestRunner case","description":"A test case: the steps a person takes, ending at a checkpoint.",
 "type":"object","required":["name","steps"],"properties":{"$schema":{"type":"string"},"name":text("What the case does, as the launcher lists it."),
 "needs":{"type":"array","items":{"type":"string"},"description":"The ids of the cases it carries on from (their file names without the number)."},
 "needsNote":text("What it needs, in words, for the launcher's Run after column, such as Any time."),
 "covers":text("What it tries, for the launcher."),
 "tags":{"type":"array","items":{"type":"string"},"description":"Words to pick cases by, such as smoke: the launcher filters by them, and autotest run --tags runs only those cases."},
 "review":text("Why the case needs looking at again, such as a change to a case it carries on from. The launcher shows a warning on it until it is changed, or the warning is cleared."),"steps":{"$ref":"#/definitions/steps"},
 "reset":{"$ref":"#/definitions/steps","description":"Steps that take the app back to where this case leaves it, such as going to the home screen after signing in. They play after its checkpoint, and again before each case that starts after its reset, instead of starting the app again. No checkpoint."},
 "afterReset":text("The id of the case this one starts after the reset of, such as log-in: once that case has run, its reset steps take the app back there. When it hasn't run, the run plays it first. Not with needs.")},"additionalProperties":False,"definitions":defs}
macro = {"$schema":S,"$id":"macro.schema.json","title":"AutoTestRunner macro","description":"Reusable steps with parameters, run with { \"call\": name }.",
 "type":"object","required":["steps"],"properties":{"$schema":{"type":"string"},"name":text("Its name, if not the file's."),"description":text("What it does."),
 "params":{"type":"object","additionalProperties":{"type":"object","properties":{"description":{"type":"string"},"default":{"type":["string","number","boolean"]}},"additionalProperties":False}},
 "steps":{"$ref":"case.schema.json#/definitions/steps"},"returns":text("The variable the call's as keeps.")},"additionalProperties":False}
elements = {"$schema":S,"$id":"elements.schema.json","title":"AutoTestRunner elements","description":"The app's controls by name: the page-object layer. A list is tried in order.",
 "type":"object","properties":{"$schema":{"type":"string"}},"additionalProperties":{"$ref":"case.schema.json#/definitions/target"}}
suite = {"$schema":S,"$id":"suite.schema.json","title":"AutoTestRunner suite","description":"How to start and reset an app, and the suite's variables.",
 "type":"object","required":["name","app"],"properties":{"$schema":{"type":"string"},"name":text("The suite's name."),
 "app":{"type":"object","required":["exe","window"],"properties":{"exe":text("The program to start; ${variables} allowed."),"arguments":text("What to start it with."),
   "window":text("Its main window's title."),"startWindow":text("The title of the window it opens first, when that isn't its main window, such as a sign-in window. A useWindow step moves on to the main window."),"runAsAdmin":{"type":"boolean","description":"Restarts autotest as administrator, as the app needs it."},
   "status":text("An element whose text says what the app is doing, such as a status bar. Its text before each click is kept for waitText's new.")},"additionalProperties":False},
 "variables":{"type":"object","additionalProperties":{"anyOf":[{"type":["string","number","boolean"]},{"type":"object","required":["firstExisting"],"properties":{"firstExisting":{"type":"array","items":{"type":"string"},"description":"The first path that exists, relative to the suite folder; the last when none does."}},"additionalProperties":False}]}},
 "helpers":{"type":"object","description":"${name(value)} picks something out of value. readFile is built in: ${name(readFile(path))} reads it out of a file, relative to the suite folder.",
   "propertyNames":{"not":{"pattern":"^[Rr][Ee][Aa][Dd][Ff][Ii][Ll][Ee]$"}},
   "additionalProperties":{"type":"object","oneOf":[{"required":[way]} for way in ["regex","xPath","jsonPath"]],"properties":{
     "regex":text("${name(value)} gives the regex's first group in value, or the whole match when it has no group."),
     "xPath":text("${name(value)} gives what the XPath finds first in value, which is XML, such as //database/connectionString/text(). Namespaces are left out."),
     "jsonPath":text("${name(value)} gives what the JSONPath finds first in value, which is JSON, such as $.ConnectionStrings.Orders.")},"additionalProperties":False}},
 "requires":{"type":"array","items":{"type":"string"},"description":"Paths that must exist before a run starts. If one doesn't, autotest exits with code 2."},
 "reset":{"type":"object","properties":{"script":text("A .ps1 or .cs file that removes what the suite leaves on the machine. A .cs runs with dotnet run, which needs the .NET 10 SDK."),"powershell":text("The older name for script."),"leftovers":text("A .ps1 or .cs file that prints one line for each thing an earlier run left; nothing on a clean machine.")},"additionalProperties":False},
 "databases":{"type":"object","description":"Databases that query steps and dbValue checks use, by a name of your own.","additionalProperties":{"type":"object","required":["kind","connection"],"properties":{
   "kind":{"anyOf":[{"enum":["sqlServer","postgres","oracle","mySql","sqlite"]},{"type":"string","pattern":"\\$\\{"}],"description":"sqlServer for SQL Server, Express and LocalDB; mySql for MySQL and MariaDB. It can be a ${...}, such as one read from the app's .config file, which comes to one of them when it is used."},
   "connection":text("Its connection string, as its .NET driver takes it; ${variables} allowed, so a password can come from --var.")},"additionalProperties":False}},
 "queues":{"type":"object","description":"Message queues that send, receive and purge steps and queue checks use, by a name of your own.","additionalProperties":{"type":"object","required":["kind","queue"],"properties":{
   "kind":{"enum":["rabbitMq","ibmMq","msmq"]},
   "queue":text("The queue's name, or for MSMQ its path, such as .\\private$\\orders."),
   "connection":text("The broker: amqp://user:password@host:5672/ for RabbitMQ, host(port) for IBM MQ."),
   "channel":text("IBM MQ's server connection channel, such as DEV.APP.SVRCONN."),
   "queueManager":text("IBM MQ's queue manager, such as QM1."),
   "user":text("IBM MQ's user, when the channel asks for one."),
   "password":text("IBM MQ's password; ${variables} allowed."),
   "format":{"enum":["xml","text"],"description":"How an MSMQ message's text is written: xml, as .NET's XmlMessageFormatter does (the default), or plain text."}},"additionalProperties":False}},
 "software":{"type":"object","description":"Software the suite needs on the computer, by a name of your own. The launcher looks for it when the suite is picked and offers to install what is missing; autotest setup does the same from the command line. Each has one of winget, windowsFeature or script.","additionalProperties":{"type":"object","properties":{
   "title":text("What a person calls it, such as SQL Server Express."),
   "winget":text("The winget package id, such as Microsoft.SQLServer.2022.Express."),
   "version":{"type":["string","number"],"description":"The version wanted: one that starts with it will do, such as 3.13 for 3.13.7. Left out, any version, and the latest is installed."},
   "windowsFeature":text("A Windows feature, as Enable-WindowsOptionalFeature names it, such as MSMQ-Server. Installing it needs administrator."),
   "script":text("A .ps1 or .cs file that installs it, relative to the suite folder."),
   "args":{"type":"array","items":{"type":"string"},"description":"What the script is given; ${version} is the version."},
   "present":{"$ref":"case.schema.json#/definitions/check","description":"A check that passes when it is installed, such as { \"service\": \"RabbitMQ\", \"state\": \"Running\" }. A script needs one."},
   "administrator":{"type":"boolean","description":"The script needs administrator, which is asked for before it runs."}},"additionalProperties":False}},
 "screen":{"type":"object","properties":{"width":{"type":"integer"},"height":{"type":"integer"}},"description":"The smallest screen the suite is written for."},
 "builder":{"type":"object","description":"What the builder does while recording a case.","properties":{
   "watch":{"type":"array","description":"Controls whose text the builder watches: when it changes after a step, it adds a wait for the new text. The app's status, and any element with a macro named waitFor and its name, are watched anyway.","items":{"type":"object","required":["element"],"properties":{
     "element":text("The control, by its name in elements.json."),"call":text("The macro that waits for it, given startsWith when it takes it. A waitText when left out."),
     "check":{"type":"boolean","description":"Also keeps what it said, and checks it at the checkpoint."}},"additionalProperties":False}},
   "afterClick":{"type":"array","description":"Steps the builder adds after a control is clicked, instead of a wait for the status text.","items":{"type":"object","required":["element","call"],"properties":{
     "element":text("The control clicked, by its name in elements.json."),"call":text("The macro to run after it."),
     "with":{"type":"object","additionalProperties":{"type":["string","number","boolean"]},"description":"The macro's parameters."},
     "delay":{"type":["number","string"],"description":"Seconds to wait before the macro, as a wait step."}},"additionalProperties":False}}},"additionalProperties":False}},"additionalProperties":False}
for name, doc in [("case",case),("macro",macro),("elements",elements),("suite",suite)]:
    open(f"{out}/{name}.schema.json","w").write(json.dumps(doc, indent=2, ensure_ascii=False)+"\n")
