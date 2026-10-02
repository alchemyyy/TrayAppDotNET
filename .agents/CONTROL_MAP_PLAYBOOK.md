# Control Map Playbook

Use this for keyboard navigation, tab order, key bindings, and control-map AXAML work.

A control map is one AXAML file per app that declares every interactive control as a node in a scope tree.
Document order is tab order. Nesting is focus scope. Key gestures live on the leaf they activate.
A map is the keyboard specification: `Kind` and `Keys` state what each control must handle, while the inventory
reports list where code falls short today.

## Files

- Schema types and runtime index: `TrayAppDotNETCommon/src/UI/ControlMapping`
- Shared templates (the common map): `TrayAppDotNETCommon/src/UI/ControlMap.axaml` with owner `ControlMap.cs`,
  `x:Class="TrayAppDotNETCommon.UI.ControlMap"`
- App maps: `<App>/src/UI/ControlMap.axaml` with owner `ControlMap.cs`, `x:Class="<App>.UI.ControlMap"`
- Every map class is named `ControlMap` and its namespace names the project. The base type they derive from is
  `TrayAppDotNETCommon.UI.ControlMapping.ControlMap`, which the AXAML root element `<ControlMap>` names
- Generator: `TrayAppDotNETCommon/generators/AxamlPropertyLinker/ControlMaps`
- Generator tests: `TrayAppDotNETCommon/tests/AxamlPropertyLinker.Tests/ControlMapGeneratorTests.cs`
- Runtime tests for the common map: `TrayAppDotNETCommon/tests/XmlSourceGenerator.Tests/ControlMapTests.cs`

## Extraction Model

- A leaf is any control or painted region with an activation handler. "Click handler" is the common case, but the
  activation set is broader:
  - `Click`, `Pressed`, `Tapped`, `DoubleTapped`
  - `CheckedChanged`, `IsCheckedChanged`, `SelectionChanged`, `ValueChanged`, `TextChanged`
  - `PointerPressed`, `PointerReleased`, `PointerWheelChanged`, `KeyDown`, including `AddHandler(...Event, ...)`
  - Overrides in custom controls: `OnPointerPressed`, `OnPointerReleased`, `OnPointerWheelChanged`, `OnKeyDown`
  - Callbacks passed to factories, such as `TrayAppDotNETSettingsUI.Toggle(palette, isChecked, changed)`
  - Context menu entry actions, Win32 tray icon messages, and `RegisterHotKey` actions
- A scope is an ancestor of at least one leaf. Containers with no leaf below them never appear.
- Collapse pass-through layout. Emit a scope only for a surface root, a page, a card or section holding two or more
  leaves, a toolbar or option group, a repeated item, or a painted control with internal targets.
- Order siblings in visual reading order: top to bottom, then left to right, following StackPanel orientation,
  Grid row and column, and DockPanel dock order. When a setting changes the layout, map the default layout and say
  so in a `Description`.
- Interactive controls that have no handler, such as a bound `TextBox` or a `ScrollViewer`, are exceptions to the
  click-handler rule. Map them and note the exception in the inventory report.

## Ownership

A node ID is only usable if the code that builds the control can name it. Code that builds the same structure for
several owners, such as `SettingsWindowCommon`, a card factory, `TaskManagerTablePage`, or a reorder dialog base
class, can only name template IDs. So:

- Structure one class builds for several instances is a `Template`, and each instance is a `Scope` or `Surface`
  with `Template="..."`.
- Controls an instance builds itself are that instance's children, routed into the template's slots.
- Mirror the class hierarchy. `TaskManagerGrid` holds only what the abstract `TaskManagerGridControl` handles;
  the generic table's regions live in `TaskManagerTablePage`, and the Processes canvas keeps its own.
- `Command` and `Region` leaves are never bound to a control. They document keys and pointer targets inside the
  focusable scope that handles them.

## Elements

| Element | Purpose |
|---|---|
| `ControlMap` | Root. Carries `x:Class` |
| `Template` | Reusable scope, root level only. Instantiated by `Template="..."` on a `Surface` or `Scope` |
| `Slot` | Position inside a `Template` where the instance's own children go. `ID` names it when a template has several; a template has at most one unnamed slot |
| `Surface` | Focus root: a window, popup, tray icon, or the global hotkey table. Tab never crosses surfaces |
| `Scope` | Group of nodes. Owns tab and arrow behavior for its children |
| `Leaf` | One activatable control, painted region, or keyboard-only command |
| `Variant` | Alternative child order of its parent while code activates the variant named by `ID`; `Order` lists child IDs and named slot IDs, and unlisted children are absent in that layout. Not allowed on a template instance or beside an unnamed slot |

## Attributes

| Attribute | On | Values | Default |
|---|---|---|---|
| `ID` | all but `ControlMap` | PascalCase C# identifier with acronyms uppercase (`MapHUD`, `CPUGraphMenu`), unique among siblings, never `ID`; optional on `Slot` | required |
| `Kind` | `Surface` | `Window`, `Popup`, `Tray`, `Global` | `Window` |
| `Kind` | `Leaf` | see Leaf Kinds | required |
| `Tab` | `Surface`, `Scope` | Avalonia `KeyboardNavigationMode`: `Continue`, `Local`, `Cycle`, `Contained`, `Once`, `None` | `Cycle` on surfaces, `Local` on scopes |
| `Arrows` | `Scope` | `None`, `Horizontal`, `Vertical`, `Spatial` | `None` |
| `IsFocusable` | `Scope` | The container itself takes focus, as painted tables, editors and drill-in cards do | `False` |
| `Entry` | `Scope` | `Tab`: Tab reaches the children. `Enter`: the focusable container is one stop, Enter steps in, Escape steps out | `Tab` |
| `IsRepeated` | `Scope`, `Leaf` | One runtime instance per item, such as a device card or a menu entry | `False` |
| `IsArranged` | `Scope` | The user orders the children, so tab order follows the order code adds them | `False` |
| `Order` | `Variant` | Space-separated child IDs and named slot IDs in the variant's tab order | required |
| `IsTabStop` | `Leaf` | `False` for pointer manipulators and for regions reached only by arrows or keys | `True` |
| `Template` | `Surface`, `Scope` | ID of a `Template` in this map or in the common map | none |
| `Slot` | `Scope`, `Leaf` | Named slot of the parent's template that receives this node | unnamed slot |
| `Keys` | `Leaf` | Accelerators: semicolon-separated key gestures, such as `Ctrl+F;F3`, that activate the leaf from anywhere inside `KeyScope` | none |
| `KeyScope` | `Leaf` | ID of the ancestor whose focus subtree activates `Keys` | parent |
| `FocusKeys` | `Leaf` | Semicolon-separated key gestures handled only while the leaf itself has focus, beyond the implicit keys of its `Kind`, such as `Escape;Down` on a search box | none |
| `Pointer` | `Leaf` | Semicolon-separated pointer gestures: optional `Ctrl+`, `Shift+`, `Alt+`, then `Click`, `DoubleClick`, `RightClick`, `MiddleClick`, `Wheel`, or `Drag` | none |
| `Event` | `Leaf` | Activation hook, such as `Click`, `CheckedChanged`, `PointerReleased`, `OnPointerPressed`, `Callback`, `MenuEntry`, `TrayMessage`, `Hotkey`, or `MapCommand` for a command whose handler is registered with `MapCommand` (its `Source` names the registering member) | none |
| `Source` | all nodes | `TypeName.MemberName` where the handler is attached or the container is built; constructors are `TypeName.TypeName` | none |
| `Description` | all nodes | Short statement of what the node does | none |

## Key Gestures

- A gesture is optional modifiers `Ctrl+`, `Shift+`, `Alt+`, `Win+` in that order, then one Avalonia `Key` name.
- Digits are `D0` through `D9` or `NumPad0` through `NumPad9`. A bare digit is rejected because Avalonia parses
  `1` as the enum value `Key.Cancel`.
- Use `Escape`, `Delete`, `Return` or `Enter`, `OemPlus`, `OemMinus`; abbreviations such as `Esc` and `Del` are
  not key names.
- A `KeyDown` handler on the control itself is `FocusKeys`. A handler on a window, page, or painted container that
  reacts wherever focus is inside it is `Keys` on the leaf it activates, with `KeyScope` naming that container when
  it is not the leaf's parent. A window-level Escape is a `Dismiss` command with `Keys="Escape"`.
- The shared tray icon turns Shift+F10 and the Apps key into a right click, so every tray `OpenMenu` command carries
  `Keys="Shift+F10;Apps"`.
- Two leaves may not claim the same `Keys` gesture in the same key scope.
- Tab and Shift+Tab never reach the map, because Avalonia's keyboard navigation handles them first. Express Tab
  behavior with `Tab` modes. A control that handles Tab itself, such as a curve editor cycling its points, keeps that
  in its own handler and lists it in `FocusKeys`.
- Gestures match modifiers exactly: `Ctrl+Up` does not fire on Ctrl+Shift+Up.
- A command handler receives the modifiers but not the key, so an action that depends on which key fired needs one
  command per key, such as `MoveUp` and `MoveDown`.
- While an editable text box has focus, keys that type a character (letters, digits, symbols, Space, alone or with
  Shift) go to the box and their accelerators do not fire. Ctrl, Alt, and Win accelerators, Escape, and Enter still
  do. A command handler otherwise always consumes its key.

## Leaf Kinds

Implicit keys are what the shared controls already handle while focused (`SettingsButton`, `SettingsToggle`,
`SettingsMiniToggle`, `SettingsNavItem`, `SettingsComboBox`, `SettingsNumberBox`, `FlyoutSlider`, context menu
rows). `FocusKeys` lists only keys beyond them.

| Kind | Meaning | Implicit keys |
|---|---|---|
| `Button` | Performs an action | Enter, Space |
| `Toggle` | Two-state switch or check box | Enter, Space |
| `Option` | One choice in a mutually exclusive group | Enter, Space; arrows when the scope sets `Arrows` |
| `Select` | Drop-down choice | Enter, Space, Down open it; Escape closes it while open |
| `Slider` | Continuous value | Left, Right, Up, Down, PageUp, PageDown, Home, End |
| `Number` | Numeric entry with spinners | Up, Down, with Ctrl and Ctrl+Shift for larger steps; Enter commits; Escape cancels; typing |
| `Text` | Text entry | typing; Enter commits and leaves the box |
| `Navigation` | Sidebar item or tab header that selects a page | Enter, Space |
| `ListItem` | Row in a list or menu | Enter, Space; Right opens a submenu |
| `Region` | Hit-tested area inside a painted control, such as a header cell, row, or curve point | none |
| `Handle` | Pointer manipulator, such as a resize grip, splitter, scroll bar, or drag handle | none |
| `Command` | Keyboard-only or non-visual action, such as Escape closing a flyout | none |

## Shared Templates

Common map (`TrayAppDotNETCommon/src/UI/ControlMap.axaml`) templates, by owner:

- `SettingsShell` (`SettingsWindowCommon`): caption buttons, sidebar with search, page navigation, footer
  navigation, resize handle, page host, and confirm overlay. The unnamed slot takes the app's pages;
  `NavigationActions` and `FooterActions` take the rows an app builds in `CreateSidebarNavigationActions` and
  `CreateSidebarFooterActions`. Task Manager's main window derives from the same shell
- `GeneralSection` = `StartupCard` then `InstallationSection` (`TrayAppDotNETGeneralSettingsSection`). Apps that put
  their own cards between `BuildStartupCard` and `AddInstallationSection` instantiate the two halves separately
- `RenderingSection`, `AboutPage`
- Cards: `ResettableNumberCard`, `PairToggleCard`, `SingleColorCard`, `VariantColorCard`, `CoordinatorColorCard`
- Scrolling: `ScrollBar`, `ScrollViewport`, `VerticalScrollViewport` (the viewports slot their content). The scroll
  bar's right-click menu is the common-map surface `ScrollBarMenu`, not a template
- Dialogs: `UpdateConfirmation`, `ColorPicker`, `Installer`, `Uninstaller`
- Menus: `ContextMenu`, `EditableContextMenu` (their slot takes the app's entries), `EditableMenuEntry`
- `SearchableListBox`

Single-control common widgets, such as the flyout undock button and the caption close button, are plain `Leaf`
nodes in the app map with `Source` naming the common type.

## Example

```xml
<ControlMap
    x:Class="BatteryTrayAppDotNET.UI.ControlMap"
    xmlns="using:TrayAppDotNETCommon.UI.ControlMapping"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

    <Surface ID="TrayIcon" Kind="Tray">
        <Leaf ID="ToggleFlyout" Kind="Command" Pointer="Click" Event="TrayMessage"
              Source="BatteryAvaloniaApp.OnTrayLeftClick" />
        <Leaf ID="OpenMenu" Kind="Command" Keys="Shift+F10;Apps" Pointer="RightClick" Event="TrayMessage"
              Source="BatteryAvaloniaApp.OnTrayRightClick" />
    </Surface>

    <Surface ID="Flyout" Kind="Popup">
        <Scope ID="Header" Tab="Once" Arrows="Horizontal">
            <Leaf ID="OpenSettings" Kind="Button" Event="PointerReleased" Source="BatteryFlyoutWindow.BuildHeaderActions" />
        </Scope>
    </Surface>

    <Surface ID="Settings" Template="SettingsShell">
        <Scope ID="GeneralPage">
            <Scope ID="General" Template="GeneralSection" />
            <Leaf ID="ShowPercentage" Kind="Toggle" Event="Callback" Source="BatterySettingsWindow.BuildGeneralPage" />
        </Scope>
    </Surface>
</ControlMap>
```

Named slots route an instance's children. Task Manager's table pages put header actions in the unnamed slot and App
history's extra link in `Information`:

```xml
<Scope ID="AppHistoryPage" Template="TaskManagerTablePage" Source="AppHistoryPage.AppHistoryPage">
    <Leaf ID="More" Kind="Button" Event="Click" Source="AppHistoryPage.OnMoreClick" />
    <Leaf ID="DeleteUsageHistory" Kind="Button" Event="Click" Slot="Information"
          Source="AppHistoryPage.OnDeleteHistoryClick" />
</Scope>
```

A template can forward a slot into a template it instantiates: `ReorderDialog` places `<Slot ID="RowContent" />`
inside its `ReorderList` instance, so the column chooser's `Visible` toggle (`Slot="RowContent"`) lands in every row.

## Build Pipeline

- The Avalonia XAML compiler compiles each map like any other `x:Class` AXAML. Unknown elements or attributes and
  invalid enum values fail the build with `AVLN2000` or `AVLN3000`.
- Each map has a hand-written owner, like every `x:Class` AXAML in the repo:
  `public sealed partial class ControlMap : TrayAppDotNETCommon.UI.ControlMapping.ControlMap` with
  `public ControlMap() : base(MapName) => AvaloniaXamlLoader.Load(this);`. The base is namespace-qualified because
  it shares the class name. Avalonia's name generator cannot see generated types, so a generated owner would raise
  `AXN0001`.
- `ControlMapGenerator` adds `MapName` (the namespace-qualified class name, such as
  `BatteryTrayAppDotNET.UI.ControlMap`) and one static `ControlMapNodeID` per node to the owner, nested like the
  AXAML. A scope's own ID is its `ID` member, for example `ControlMap.Flyout.Header.ID`; a leaf is a field, for
  example `ControlMap.Flyout.Header.OpenSettings`. Template nodes keep template paths, such as
  `ControlMap.SettingsShell.Sidebar.Search.SearchText` in Common; instance children keep instance paths.
- How `ControlMap` resolves in C#: inside `<App>.UI` and its child namespaces it is the app map; inside
  `TrayAppDotNETCommon.UI` and its child namespaces other than `ControlMapping` it is the common map; inside
  `ControlMapping` it is the base type. Elsewhere, let the namespace say which: `UI.ControlMap` from `<App>`, and
  `TrayAppDotNETCommon.UI.ControlMap` for common IDs in app code.
- `TrayAppDotNET.Parent.targets` passes the common map to every app compilation as an `AdditionalFiles`
  item with `TrayAppDotNETControlMapReference="true"`, so app maps resolve shared template IDs and slot names at
  build time. The generator emits no code for a reference map.
- Generator diagnostics, all errors:

| ID | Rule |
|---|---|
| `TADNCM001` | Malformed XML, or two maps declare one `x:Class` |
| `TADNCM002` | Unknown element, or an element outside its allowed parent (a `Slot` outside a `Template`) |
| `TADNCM003` | Attribute not valid on that element |
| `TADNCM004` | ID or slot name is not PascalCase ASCII, an ID is `ID`, equals its parent's ID, collides with a generated member or the class name `ControlMap`, or redefines a reference template |
| `TADNCM005` | Two siblings share an ID |
| `TADNCM006` | Enum or boolean value not in the schema; enum names are case-sensitive |
| `TADNCM007` | Malformed `Keys`, `FocusKeys`, or `Pointer` gesture |
| `TADNCM008` | `KeyScope` without `Keys`, or naming something other than an ancestor |
| `TADNCM009` | `Template` names no template in this map or a reference map |
| `TADNCM010` | A template repeats a slot name, a child names a slot its parent's template lacks, or `Slot` appears outside a template instance |
| `TADNCM011` | Missing `x:Class`, `ID`, or `Leaf` `Kind` |
| `TADNCM012` | `Entry="Enter"` on a scope that is not `IsFocusable="True"` |
| `TADNCM013` | Two leaves claim one `Keys` gesture in one key scope; `Enter` and `Return` are the same key |
| `TADNCM014` | A template, surface, or scope with no children and no `Template` |
| `TADNCM015` | A template instantiates itself |
| `TADNCM016` | The `x:Class` has no hand-written partial owner deriving from `TrayAppDotNETCommon.UI.ControlMapping.ControlMap` |
| `TADNCM017` | A variant on a template instance or beside an unnamed slot, or an `Order` naming an unknown, repeated, or no child |
| `TADNCM100` | Warning from `ControlMapTagAnalyzer`: a node the runtime needs is never tagged in code. Required are window and popup surfaces, scopes with `IsRepeated`, `IsArranged`, `IsFocusable`, `Entry`, a non-`Local` `Tab` or `Arrows`, keyboard leaves that are tab stops, commands with `Keys`, and template instances whose template the map instantiates more than once. Nothing under a `Tray` or `Global` surface is required, because Win32 delivers that input |

## Runtime

Each generated map registers itself with `ControlMapCatalog` from a module initializer and is built from its compiled
XAML on first use. `ControlMapForest` expands every map: templates inlined at each instance, slots filled.

Tagging a control (`ControlMapBinding`, namespace `TrayAppDotNETCommon.UI.ControlMapping`):

| Call | Use |
|---|---|
| `control.MapTo(id)` | Tag the control that a node represents, where it is constructed. Windows take their surface ID |
| `control.MapActivation(activation => ...)` | How the control activates; Enter/Space on the focused control and accelerators targeting it run this. `FlyoutButtonState`, `SettingsButton`, `SettingsToggle`, `SettingsNavItem`, `SettingsSwatch`, `SettingsMiniToggle`, `SettingsComboBox`, and the caption close button register their own |
| `owner.MapCommand(id, handler)` | Handler of a `Command` leaf. Register it on the window or on the control bound to the command's key scope |
| `control.MapVariant(ControlMap.Variants.HeaderOnTop, isActive)` | Activate a layout variant for a control and everything below it |
| `ControlActivation` | `Source` and `KeyModifiers`, from a click or a key, but not the key itself; flyout button callbacks receive it |

Factories take the ID as a parameter because they create the control internally:

- Settings cards take `node:`. `BoolCard`, `IntCard`, `DoubleCard`, `ComboCard`, and `StringComboCard` tag their
  inner control; `ResettableDoubleCard`, `PairBoolCard`, `SingleColorCard`, `VariantColorCard`, and
  `TrayAppDotNETSettingsColorCardCoordinator.ColorCard` tag the card with the instance ID.
- `SettingsVerticalScrollViewport`, `SettingsScrollViewport`, and `SettingsSearchableListBox` take `node:` for the
  instance; `SettingsScrollHost` and `TrayAppDotNETSettingsUI.ScrollHost` take `scrollBarNode:`.
- `ContextMenuEntry` and `EditableContextMenuEntry` take `Node = ...` for their row; `ContextMenuEntry.SubmenuNode`
  names a submenu window's surface.
- Windows the common code opens for an app take their surface through options:
  `TrayAppDotNETUpdatePromptOptions.Node` and `TrayAppDotNETAboutPageOptions.UpdatePromptNode`.

What the runtime derives once a tagged control joins a window:

- Tab index: the node's preorder position inside its nearest tagged group, under the active variants. A scope that
  has no control of its own still orders its children correctly, so tag scopes only when the analyzer requires it.
- `KeyboardNavigation.TabNavigation` from `Tab`; `Focusable` and `IsTabStop` for keyboard leaves and focusable
  scopes; a two-tone focus ring for controls that are not templated. A Text or Number leaf that wraps a `TextBox`
  becomes one stop on the box.
- Key dispatch per window, after the focused control has had the key: Enter and Space activate a focused clickable
  leaf through its activation, `Arrows` move focus within the scope, `Entry="Enter"` steps in and out, and `Keys`
  accelerators run from the innermost key scope outward.

Resolution: a template node used by several instances resolves through the nearest tagged ancestor, so the code that
instantiates a template more than once must tag each instance container with its instance ID. The analyzer
enforces this.

## Tagging Rules

- Tag where the control is constructed, with the ID the constructing code can name: template IDs in shared code,
  instance IDs in the code that instantiates a template, app IDs in app code.
- A window or popup surface is tagged with its surface ID. Common windows tag themselves with their template root;
  an app that opens several instances of one common window tags each with its own surface ID after construction.
- A gesture the map owns as `Keys` is dispatched by the map. Delete the ad hoc `KeyDown` code that handled it and
  register the action with `MapCommand` instead. `FocusKeys` stay in the control's own handler.
- Layout settings that reorder or remove children use a `Variant` on the affected container; code calls
  `MapVariant` with the generated name when it builds that layout.
- `IsArranged="True"` on a scope whose children the user orders (a saved toolbar arrangement); the children keep the
  order code adds them in.
- A control carries one node, and a tagged leaf owns everything inside it, both for resolution and for the drift
  check. Leaves therefore cannot nest: a clickable card that contains other leaves is a scope, or a pointer-only
  `Region` whose children give the keyboard path. Leave a `Handle` untagged when its control contains tagged
  controls.
- Done means: zero `TADNCM100` warnings for the project, and no `Control map: unmapped` lines in the Debug log for
  the surfaces the change touches. An up-to-date incremental build prints no analyzer warnings, so count them after
  `dotnet build <project> --no-dependencies --no-incremental`.
- Agents working in parallel run one `dotnet` build at a time; concurrent builds race on the shared
  `TrayAppDotNETCommon` output.

## Maintenance Rules

- Adding, removing, or reordering an interactive control means editing the app map in the same change.
- Keep `Source` current when a handler moves.
- A new key gesture goes on the leaf it activates. Do not add ad hoc `KeyDown` checks for gestures the map can own.
- Task Manager table gestures live only in the map. `TaskManagerGridShortcuts` pairs each action with its leaf (the
  `TaskManagerGrid` commands and the Processes `Table.EndTask`) and reads the leaf's `Pointer` and `Keys` through
  `ControlMapCatalog`, so the tables' wheel and middle-click dispatch, where the gesture requiring the most held
  modifiers wins and equal matches do nothing, and the Hotkeys settings page show the same gestures. Key gestures
  dispatch through `MapCommand` on the tagged table. Change a gesture by editing its leaf; a new gesture leaf also
  needs an action in `TaskManagerGridShortcuts`, which its tests enforce.
