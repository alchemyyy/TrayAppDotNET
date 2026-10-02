using Microsoft.CodeAnalysis;

namespace TrayAppDotNETCommon.AxamlPropertyLinker;

/// <summary>Template facts the validator needs from every control map visible to the compilation.</summary>
internal sealed class ControlMapTemplateInfo(string id, string mapName, HashSet<string> slotNames)
{
    public readonly string ID = id;
    public readonly string MapName = mapName;

    // An unnamed slot appears as the empty string
    public readonly HashSet<string> SlotNames = slotNames;
}

/// <summary>
/// Structural rules the Avalonia XAML compiler cannot check: element placement, ids, slots, template references,
/// key scopes, gesture syntax, and accelerator conflicts. Attribute names and enum values are checked here too,
/// so a map fails with a control map diagnostic before XAML compilation runs.
/// </summary>
internal static class ControlMapValidator
{
    public const string TemplateElement = "Template";
    public const string SurfaceElement = "Surface";
    public const string ScopeElement = "Scope";
    public const string LeafElement = "Leaf";
    public const string SlotElement = "Slot";
    public const string VariantElement = "Variant";

    private const string IDAttribute = ControlMapElement.IDAttribute;
    private const string OrderAttribute = "Order";
    private const char OrderSeparator = ' ';
    private const string KindAttribute = "Kind";
    private const string TabAttribute = "Tab";
    private const string ArrowsAttribute = "Arrows";
    private const string IsFocusableAttribute = "IsFocusable";
    private const string EntryAttribute = "Entry";
    private const string IsRepeatedAttribute = "IsRepeated";
    private const string IsArrangedAttribute = "IsArranged";
    private const string IsTabStopAttribute = "IsTabStop";
    private const string TemplateAttribute = "Template";
    private const string KeysAttribute = "Keys";
    private const string KeyScopeAttribute = "KeyScope";
    private const string FocusKeysAttribute = "FocusKeys";
    private const string PointerAttribute = "Pointer";
    private const string EventAttribute = "Event";
    private const string SlotAttribute = "Slot";
    private const string SourceAttribute = "Source";
    private const string DescriptionAttribute = "Description";
    private const string TrueValue = "True";
    private const string EnterEntry = "Enter";
    private const string UnnamedSlot = "";
    private const string ListSeparator = ", ";

    private static readonly string[] ElementNames =
        [TemplateElement, SurfaceElement, ScopeElement, LeafElement, SlotElement, VariantElement];

    private static readonly string[] TemplateAttributes = [IDAttribute, SourceAttribute, DescriptionAttribute];

    private static readonly string[] SurfaceAttributes =
        [IDAttribute, KindAttribute, TabAttribute, TemplateAttribute, SourceAttribute, DescriptionAttribute];

    private static readonly string[] ScopeAttributes =
    [
        IDAttribute, TabAttribute, ArrowsAttribute, IsFocusableAttribute, EntryAttribute, IsRepeatedAttribute,
        IsArrangedAttribute, TemplateAttribute, SlotAttribute, SourceAttribute, DescriptionAttribute
    ];

    private static readonly string[] LeafAttributes =
    [
        IDAttribute, KindAttribute, IsTabStopAttribute, IsRepeatedAttribute, KeysAttribute, KeyScopeAttribute,
        FocusKeysAttribute, PointerAttribute, EventAttribute, SlotAttribute, SourceAttribute, DescriptionAttribute
    ];

    private static readonly string[] SlotAttributes = [IDAttribute];

    private static readonly string[] VariantAttributes = [IDAttribute, OrderAttribute, DescriptionAttribute];

    // These mirror the enums in TrayAppDotNETCommon.UI.ControlMapping and Avalonia.Input.KeyboardNavigationMode.
    // Drift cannot go unnoticed: the Avalonia XAML compiler rejects any value the CLR enums lack
    private static readonly string[] SurfaceKinds = ["Window", "Popup", "Tray", "Global"];

    private static readonly string[] LeafKinds =
    [
        "Button", "Toggle", "Option", "Select", "Slider", "Number", "Text", "Navigation", "ListItem", "Region",
        "Handle", "Command"
    ];

    private static readonly string[] TabModes = ["Continue", "Local", "Cycle", "Contained", "Once", "None"];
    private static readonly string[] ArrowModes = ["None", "Horizontal", "Vertical", "Spatial"];
    private static readonly string[] EntryModes = ["Tab", EnterEntry];
    private static readonly string[] BooleanValues = [TrueValue, "False"];

    // Members of the generated map class and its ControlMap base that a root-level id would collide with
    private static readonly string[] ReservedRootIDs =
    [
        "MapName", "Name", "Children", "Nodes", "Find", "FindTemplate", "Variants", "RegisterControlMap"
    ];

    /// <summary>Validates one document against its own templates and the templates of reference maps.</summary>
    public static void Validate(
        ControlMapDocument document,
        IReadOnlyDictionary<string, ControlMapTemplateInfo> externalTemplates,
        List<Diagnostic> diagnostics)
    {
        Dictionary<string, ControlMapElement> localTemplates = new(StringComparer.Ordinal);
        foreach (ControlMapElement rootElement in document.RootElements)
        {
            if (!string.Equals(rootElement.Name, TemplateElement, StringComparison.Ordinal)) continue;
            if (rootElement.ID.Length == 0 || localTemplates.ContainsKey(rootElement.ID)) continue;

            localTemplates.Add(rootElement.ID, rootElement);
            if (!externalTemplates.TryGetValue(rootElement.ID, out ControlMapTemplateInfo? shadowed)) continue;

            diagnostics.Add(ControlMapDiagnostics.At(
                ControlMapDiagnostics.InvalidID,
                document.Path,
                rootElement.Line,
                rootElement.Column,
                rootElement.ID,
                $"redefines the template of the same id in {shadowed.MapName}"));
        }

        ValidateSiblings(document, document.RootElements, diagnostics);
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, document.RootElements);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            if (!ValidateElement(document, element, localTemplates, externalTemplates, diagnostics)) continue;

            ValidateSiblings(document, element.Children, diagnostics);
            PushChildren(pending, element.Children);
        }

        ValidateAccelerators(document, diagnostics);
        ValidateTemplateCycles(document, localTemplates, diagnostics);
    }

    // Returns false for an unknown element, whose subtree is then skipped
    private static bool ValidateElement(
        ControlMapDocument document,
        ControlMapElement element,
        Dictionary<string, ControlMapElement> localTemplates,
        IReadOnlyDictionary<string, ControlMapTemplateInfo> externalTemplates,
        List<Diagnostic> diagnostics)
    {
        string[]? allowedAttributes = element.Name switch
        {
            TemplateElement => TemplateAttributes,
            SurfaceElement => SurfaceAttributes,
            ScopeElement => ScopeAttributes,
            LeafElement => LeafAttributes,
            SlotElement => SlotAttributes,
            VariantElement => VariantAttributes,
            _ => null
        };

        if (allowedAttributes == null)
        {
            Report(diagnostics, ControlMapDiagnostics.UnexpectedElement, document, element,
                element.Name, "here; control maps contain " + string.Join(ListSeparator, ElementNames));
            return false;
        }

        ValidatePlacement(document, element, diagnostics);
        foreach (ControlMapAttribute attribute in element.Attributes)
        {
            if (Array.IndexOf(allowedAttributes, attribute.Name) >= 0) continue;

            Report(diagnostics, ControlMapDiagnostics.UnknownAttribute, document, attribute,
                attribute.Name, element.Name,
                allowedAttributes.Length == 0 ? "none" : string.Join(ListSeparator, allowedAttributes));
        }

        switch (element.Name)
        {
            case TemplateElement:
                ValidateID(document, element, diagnostics);
                ValidateTemplateSlots(document, element, diagnostics);
                if (element.Children.Count == 0)
                {
                    Report(diagnostics, ControlMapDiagnostics.EmptyContainer, document, element,
                        element.Name, element.ID, string.Empty);
                }

                break;
            case SurfaceElement:
                ValidateID(document, element, diagnostics);
                ValidateValue(document, element, KindAttribute, SurfaceKinds, diagnostics);
                ValidateValue(document, element, TabAttribute, TabModes, diagnostics);
                ValidateTemplateReference(document, element, localTemplates, externalTemplates, diagnostics);
                break;
            case ScopeElement:
                ValidateID(document, element, diagnostics);
                ValidateValue(document, element, TabAttribute, TabModes, diagnostics);
                ValidateValue(document, element, ArrowsAttribute, ArrowModes, diagnostics);
                ValidateValue(document, element, IsFocusableAttribute, BooleanValues, diagnostics);
                ValidateValue(document, element, EntryAttribute, EntryModes, diagnostics);
                ValidateValue(document, element, IsRepeatedAttribute, BooleanValues, diagnostics);
                ValidateValue(document, element, IsArrangedAttribute, BooleanValues, diagnostics);
                ValidateTemplateReference(document, element, localTemplates, externalTemplates, diagnostics);
                ValidateEntry(document, element, diagnostics);
                ValidateSlotRouting(document, element, diagnostics);
                break;
            case LeafElement:
                ValidateID(document, element, diagnostics);
                ValidateLeaf(document, element, diagnostics);
                ValidateSlotRouting(document, element, diagnostics);
                break;
            case SlotElement:
                ValidateSlotName(document, element, diagnostics);
                break;
            case VariantElement:
                ValidateID(document, element, diagnostics);
                ValidateVariant(document, element, diagnostics);
                break;
        }

        return true;
    }

    // A variant reorders its parent's own children, so the parent must own them: no template instance, and no
    // unnamed slot whose content the order could not name
    private static void ValidateVariant(
        ControlMapDocument document,
        ControlMapElement variant,
        List<Diagnostic> diagnostics)
    {
        ControlMapElement? parent = variant.Parent;
        if (parent == null) return;

        if (parent.GetAttribute(TemplateAttribute) != null)
        {
            Report(diagnostics, ControlMapDiagnostics.InvalidVariant, document, variant,
                $"'{PathOf(parent)}' instantiates a template; declare the variant in the template instead");
            return;
        }

        HashSet<string> nameable = new(StringComparer.Ordinal);
        foreach (ControlMapElement sibling in parent.Children)
        {
            if (string.Equals(sibling.Name, VariantElement, StringComparison.Ordinal)) continue;

            if (string.Equals(sibling.Name, SlotElement, StringComparison.Ordinal) && sibling.ID.Length == 0)
            {
                Report(diagnostics, ControlMapDiagnostics.InvalidVariant, document, variant,
                    $"'{PathOf(parent)}' has an unnamed Slot, which a variant order cannot place");
                return;
            }

            if (sibling.ID.Length > 0)
                nameable.Add(sibling.ID);
        }

        ControlMapAttribute? order = variant.GetAttribute(OrderAttribute);
        if (order == null)
        {
            Report(diagnostics, ControlMapDiagnostics.MissingAttribute, document, variant, VariantElement,
                OrderAttribute);
            return;
        }

        HashSet<string> listed = new(StringComparer.Ordinal);
        int listedCount = 0;
        foreach (string orderID in order.Value.Split(OrderSeparator))
        {
            if (orderID.Length == 0) continue;

            listedCount++;
            if (!nameable.Contains(orderID))
            {
                Report(diagnostics, ControlMapDiagnostics.InvalidVariant, document, order,
                    $"Order names '{orderID}', which is not a child or named slot of '{PathOf(parent)}'");
            }
            else if (!listed.Add(orderID))
            {
                Report(diagnostics, ControlMapDiagnostics.InvalidVariant, document, order,
                    $"Order lists '{orderID}' twice");
            }
        }

        if (listedCount == 0)
        {
            Report(diagnostics, ControlMapDiagnostics.InvalidVariant, document, order,
                "Order lists no children");
        }
    }

    /// <summary>Returns the distinct variant names a document declares, in document order.</summary>
    public static List<string> CollectVariantNames(ControlMapDocument document)
    {
        List<string> names = [];
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, document.RootElements);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            PushChildren(pending, element.Children);
            if (!string.Equals(element.Name, VariantElement, StringComparison.Ordinal) || element.ID.Length == 0)
                continue;
            if (!names.Contains(element.ID))
                names.Add(element.ID);
        }

        return names;
    }

    // A named slot needs an identifier name; its parent template guarantees name uniqueness
    private static void ValidateSlotName(
        ControlMapDocument document,
        ControlMapElement slot,
        List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? name = slot.GetAttribute(IDAttribute);
        if (name == null || IsPascalIdentifier(name.Value)) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidID, document, name,
            name.Value, "must be a PascalCase identifier of ASCII letters and digits");
    }

    // Slot routes a child into a named slot, so it only means something under a template instance
    private static void ValidateSlotRouting(
        ControlMapDocument document,
        ControlMapElement element,
        List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? slot = element.GetAttribute(SlotAttribute);
        if (slot == null || element.Parent?.GetAttribute(TemplateAttribute) != null) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidSlot, document, slot,
            $"Slot=\"{slot.Value}\" only routes the children of a node that instantiates a Template");
    }

    private static void ValidatePlacement(
        ControlMapDocument document,
        ControlMapElement element,
        List<Diagnostic> diagnostics)
    {
        string? parentName = element.Parent?.Name;
        bool isInsideTemplate = FindTemplateRoot(element) != null;
        bool isAllowed = element.Name switch
        {
            TemplateElement or SurfaceElement => parentName == null,
            ScopeElement or LeafElement or VariantElement =>
                parentName is TemplateElement or SurfaceElement or ScopeElement,
            SlotElement => parentName is TemplateElement or ScopeElement && isInsideTemplate,
            _ => false
        };
        if (isAllowed) return;

        string placement;
        if (parentName == null)
            placement = "at the root; the root holds Template and Surface elements";
        else if (element.Name == SlotElement && !isInsideTemplate)
            placement = "outside a Template";
        else
            placement = $"inside <{parentName}>";

        Report(diagnostics, ControlMapDiagnostics.UnexpectedElement, document, element, element.Name, placement);
    }

    private static void ValidateID(ControlMapDocument document, ControlMapElement element, List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? attribute = element.GetAttribute(IDAttribute);
        if (attribute == null)
        {
            Report(diagnostics, ControlMapDiagnostics.MissingAttribute, document, element, element.Name, IDAttribute);
            return;
        }

        string id = attribute.Value;
        string? problem = null;
        if (!IsPascalIdentifier(id))
            problem = "must be a PascalCase identifier of ASCII letters and digits";
        else if (string.Equals(id, IDAttribute, StringComparison.Ordinal))
            problem = "is reserved for the generated id of each scope";
        else if (element.Parent != null && string.Equals(id, element.Parent.ID, StringComparison.Ordinal))
            problem = "matches its parent's id; C# forbids a member named like its enclosing class";
        else if (element.Parent == null
                 && (string.Equals(id, document.ClassName, StringComparison.Ordinal)
                     || Array.IndexOf(ReservedRootIDs, id) >= 0))
            problem = "collides with a member of the generated map class";

        if (problem == null) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidID, document, attribute, id, problem);
    }

    private static void ValidateSiblings(
        ControlMapDocument document,
        List<ControlMapElement> siblings,
        List<Diagnostic> diagnostics)
    {
        Dictionary<string, ControlMapElement> siblingsByID = new(StringComparer.Ordinal);
        foreach (ControlMapElement sibling in siblings)
        {
            string id = sibling.ID;
            if (id.Length == 0) continue;

            if (siblingsByID.TryGetValue(id, out ControlMapElement? first))
            {
                Report(diagnostics, ControlMapDiagnostics.DuplicateID, document, sibling, id, first.Line);
                continue;
            }

            siblingsByID.Add(id, sibling);
        }
    }

    private static void ValidateValue(
        ControlMapDocument document,
        ControlMapElement element,
        string attributeName,
        string[] allowedValues,
        List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? attribute = element.GetAttribute(attributeName);
        if (attribute == null) return;

        // XAML reads booleans case-insensitively; enum names must match exactly
        StringComparison comparison = ReferenceEquals(allowedValues, BooleanValues)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        foreach (string allowedValue in allowedValues)
        {
            if (string.Equals(attribute.Value, allowedValue, comparison))
                return;
        }

        Report(diagnostics, ControlMapDiagnostics.InvalidValue, document, attribute,
            attribute.Value, element.Name + "." + attributeName, string.Join(ListSeparator, allowedValues));
    }

    private static void ValidateEntry(ControlMapDocument document, ControlMapElement element, List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? entry = element.GetAttribute(EntryAttribute);
        if (entry == null || !string.Equals(entry.Value, EnterEntry, StringComparison.Ordinal)) return;
        if (string.Equals(element.GetValue(IsFocusableAttribute), TrueValue, StringComparison.OrdinalIgnoreCase)) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidCombination, document, entry,
            "Entry=\"Enter\" needs IsFocusable=\"True\": the container must hold focus before Enter can step in");
    }

    private static void ValidateLeaf(ControlMapDocument document, ControlMapElement element, List<Diagnostic> diagnostics)
    {
        if (element.GetAttribute(KindAttribute) == null)
            Report(diagnostics, ControlMapDiagnostics.MissingAttribute, document, element, element.Name, KindAttribute);
        else
            ValidateValue(document, element, KindAttribute, LeafKinds, diagnostics);

        ValidateValue(document, element, IsTabStopAttribute, BooleanValues, diagnostics);
        ValidateValue(document, element, IsRepeatedAttribute, BooleanValues, diagnostics);
        ValidateKeyList(document, element, KeysAttribute, diagnostics);
        ValidateKeyList(document, element, FocusKeysAttribute, diagnostics);

        ControlMapAttribute? pointer = element.GetAttribute(PointerAttribute);
        string? pointerError = pointer == null ? null : ControlMapGestures.ParsePointer(pointer.Value);
        if (pointer != null && pointerError != null)
        {
            Report(diagnostics, ControlMapDiagnostics.InvalidGesture, document, pointer,
                PointerAttribute, pointer.Value, pointerError);
        }

        ControlMapAttribute? keyScope = element.GetAttribute(KeyScopeAttribute);
        if (keyScope == null) return;

        if (element.GetAttribute(KeysAttribute) == null)
        {
            Report(diagnostics, ControlMapDiagnostics.InvalidKeyScope, document, keyScope,
                "KeyScope only applies to Keys, and this leaf has none");
            return;
        }

        if (FindKeyScope(element, keyScope.Value) != null) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidKeyScope, document, keyScope,
            $"KeyScope '{keyScope.Value}' is not the id of an ancestor of '{PathOf(element)}'");
    }

    private static void ValidateKeyList(
        ControlMapDocument document,
        ControlMapElement element,
        string attributeName,
        List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? attribute = element.GetAttribute(attributeName);
        if (attribute == null) return;

        string? error = ControlMapGestures.ParseKeys(attribute.Value, []);
        if (error == null) return;

        Report(diagnostics, ControlMapDiagnostics.InvalidGesture, document, attribute,
            attributeName, attribute.Value, error);
    }

    private static void ValidateTemplateReference(
        ControlMapDocument document,
        ControlMapElement element,
        Dictionary<string, ControlMapElement> localTemplates,
        IReadOnlyDictionary<string, ControlMapTemplateInfo> externalTemplates,
        List<Diagnostic> diagnostics)
    {
        ControlMapAttribute? reference = element.GetAttribute(TemplateAttribute);
        if (reference == null)
        {
            if (element.Children.Count > 0) return;

            Report(diagnostics, ControlMapDiagnostics.EmptyContainer, document, element,
                element.Name, element.ID, " and no Template; containers exist only above leaves");
            return;
        }

        HashSet<string> slotNames;
        if (localTemplates.TryGetValue(reference.Value, out ControlMapElement? localTemplate))
            slotNames = CollectSlotNames(localTemplate);
        else if (externalTemplates.TryGetValue(reference.Value, out ControlMapTemplateInfo? externalTemplate))
            slotNames = externalTemplate.SlotNames;
        else
        {
            string scopeNote = externalTemplates.Count == 0
                ? " and no reference control map is visible to this project"
                : " or in any reference control map";
            Report(diagnostics, ControlMapDiagnostics.UnknownTemplate, document, reference, reference.Value, scopeNote);
            return;
        }

        // Each child lands in the slot it names, or in the unnamed slot; a variant here is reported on its own
        foreach (ControlMapElement child in element.Children)
        {
            if (string.Equals(child.Name, VariantElement, StringComparison.Ordinal)) continue;

            string slotName = child.GetValue(SlotAttribute) ?? UnnamedSlot;
            if (slotNames.Contains(slotName)) continue;

            string missing = slotName.Length == 0 ? "an unnamed Slot" : $"a Slot named '{slotName}'";
            Report(diagnostics, ControlMapDiagnostics.InvalidSlot, document, child,
                $"'{PathOf(child)}' needs {missing}, and template '{reference.Value}' has none");
        }
    }

    // Slot names identify insertion points, so each may appear once per template, the unnamed one included
    private static void ValidateTemplateSlots(
        ControlMapDocument document,
        ControlMapElement template,
        List<Diagnostic> diagnostics)
    {
        HashSet<string> slotNames = new(StringComparer.Ordinal);
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, template.Children);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            PushChildren(pending, element.Children);
            if (!string.Equals(element.Name, SlotElement, StringComparison.Ordinal)) continue;
            if (slotNames.Add(element.ID)) continue;

            string duplicate = element.ID.Length == 0 ? "more than one unnamed Slot" : $"two Slots named '{element.ID}'";
            Report(diagnostics, ControlMapDiagnostics.InvalidSlot, document, element,
                $"Template '{template.ID}' has {duplicate}");
        }
    }

    // Two leaves may not claim one accelerator in the same key scope
    private static void ValidateAccelerators(ControlMapDocument document, List<Diagnostic> diagnostics)
    {
        Dictionary<(ControlMapElement Scope, string Gesture), ControlMapElement> claims = [];
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, document.RootElements);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            PushChildren(pending, element.Children);
            if (!string.Equals(element.Name, LeafElement, StringComparison.Ordinal)) continue;

            string? keys = element.GetValue(KeysAttribute);
            if (keys == null || element.Parent == null) continue;

            List<string> gestures = [];
            if (ControlMapGestures.ParseKeys(keys, gestures) != null) continue;

            string? keyScopeID = element.GetValue(KeyScopeAttribute);
            ControlMapElement? scope = keyScopeID == null ? element.Parent : FindKeyScope(element, keyScopeID);
            if (scope == null) continue;

            foreach (string gesture in gestures)
            {
                if (!claims.TryGetValue((scope, gesture), out ControlMapElement? owner))
                {
                    claims.Add((scope, gesture), element);
                    continue;
                }

                if (ReferenceEquals(owner, element)) continue;

                Report(diagnostics, ControlMapDiagnostics.DuplicateGesture, document, element,
                    gesture, PathOf(owner), PathOf(scope));
            }
        }
    }

    // A local template may not reach itself through Template attributes on its descendants
    private static void ValidateTemplateCycles(
        ControlMapDocument document,
        Dictionary<string, ControlMapElement> localTemplates,
        List<Diagnostic> diagnostics)
    {
        Dictionary<string, List<string>> edges = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ControlMapElement> template in localTemplates)
        {
            List<string> targets = [];
            Stack<ControlMapElement> pending = new();
            PushChildren(pending, template.Value.Children);
            while (pending.Count > 0)
            {
                ControlMapElement element = pending.Pop();
                PushChildren(pending, element.Children);
                string? target = element.GetValue(TemplateAttribute);
                if (target != null && localTemplates.ContainsKey(target) && !targets.Contains(target))
                    targets.Add(target);
            }

            edges.Add(template.Key, targets);
        }

        foreach (string start in edges.Keys)
        {
            List<string>? cycle = FindCycle(start, edges);
            if (cycle == null) continue;

            Report(diagnostics, ControlMapDiagnostics.TemplateCycle, document, localTemplates[start],
                start, string.Join(" -> ", cycle));
        }
    }

    // Breadth-first search for a path from start back to start
    private static List<string>? FindCycle(string start, Dictionary<string, List<string>> edges)
    {
        Dictionary<string, string> previous = new(StringComparer.Ordinal);
        Queue<string> frontier = new();
        foreach (string target in edges[start])
        {
            if (string.Equals(target, start, StringComparison.Ordinal)) return [start, start];
            if (previous.ContainsKey(target)) continue;

            previous.Add(target, start);
            frontier.Enqueue(target);
        }

        while (frontier.Count > 0)
        {
            string current = frontier.Dequeue();
            foreach (string target in edges[current])
            {
                if (string.Equals(target, start, StringComparison.Ordinal))
                {
                    List<string> cycle = [start];
                    for (string step = current; !string.Equals(step, start, StringComparison.Ordinal); step = previous[step])
                        cycle.Insert(index: 1, step);

                    cycle.Add(start);
                    return cycle;
                }

                if (previous.ContainsKey(target)) continue;

                previous.Add(target, current);
                frontier.Enqueue(target);
            }
        }

        return null;
    }

    private static ControlMapElement? FindKeyScope(ControlMapElement leaf, string keyScopeID)
    {
        for (ControlMapElement? ancestor = leaf.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (string.Equals(ancestor.ID, keyScopeID, StringComparison.Ordinal))
                return ancestor;
        }

        return null;
    }

    private static ControlMapElement? FindTemplateRoot(ControlMapElement element)
    {
        ControlMapElement? root = element;
        while (root?.Parent != null) root = root.Parent;

        return root != null && string.Equals(root.Name, TemplateElement, StringComparison.Ordinal) ? root : null;
    }

    /// <summary>Returns the dot-separated path of ids from the root-level node, skipping slots.</summary>
    public static string PathOf(ControlMapElement element)
    {
        List<string> ids = [];
        for (ControlMapElement? current = element; current != null; current = current.Parent)
        {
            if (current.ID.Length > 0)
                ids.Insert(index: 0, current.ID);
        }

        return string.Join(".", ids);
    }

    /// <summary>Returns the names of every slot below a template element; an unnamed slot is the empty string.</summary>
    public static HashSet<string> CollectSlotNames(ControlMapElement template)
    {
        HashSet<string> slotNames = new(StringComparer.Ordinal);
        Stack<ControlMapElement> pending = new();
        PushChildren(pending, template.Children);
        while (pending.Count > 0)
        {
            ControlMapElement element = pending.Pop();
            if (string.Equals(element.Name, SlotElement, StringComparison.Ordinal))
                slotNames.Add(element.ID);
            else
                PushChildren(pending, element.Children);
        }

        return slotNames;
    }

    private static bool IsPascalIdentifier(string value)
    {
        if (value.Length == 0 || value[0] < 'A' || value[0] > 'Z') return false;

        foreach (char character in value)
        {
            bool isAsciiLetterOrDigit = character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9';
            if (!isAsciiLetterOrDigit)
                return false;
        }

        return true;
    }

    private static void PushChildren(Stack<ControlMapElement> pending, List<ControlMapElement> children)
    {
        for (int childIndex = children.Count - 1; childIndex >= 0; childIndex--)
            pending.Push(children[childIndex]);
    }

    private static void Report(
        List<Diagnostic> diagnostics,
        DiagnosticDescriptor descriptor,
        ControlMapDocument document,
        ControlMapElement element,
        params object[] arguments) =>
        diagnostics.Add(ControlMapDiagnostics.At(descriptor, document.Path, element.Line, element.Column, arguments));

    private static void Report(
        List<Diagnostic> diagnostics,
        DiagnosticDescriptor descriptor,
        ControlMapDocument document,
        ControlMapAttribute attribute,
        params object[] arguments) =>
        diagnostics.Add(ControlMapDiagnostics.At(descriptor, document.Path, attribute.Line, attribute.Column, arguments));
}
