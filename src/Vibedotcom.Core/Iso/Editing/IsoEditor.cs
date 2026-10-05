using System.Collections.Immutable;

namespace Vibedotcom.Core.Iso.Editing;

/// <summary>WPF-free command facade owning documents and atomic history; spec §§2–6.</summary>
public sealed class IsoEditor : IIsoEditor
{
    private IsoPack pack;
    private IsoLayout layout;
    private readonly IsoLayout defaults;
    private IsoLayout saved;
    private readonly List<IsoLayout> undo = [], redo = [];
    private readonly HashSet<string> consumed = new(StringComparer.Ordinal);
    private long revision, counter;
    private ImmutableArray<IsoValidationIssue> validation;
    private ImmutableArray<IsoEditableAsset> palette;
    private ImmutableArray<IsoWallDecorDefinition> walls;
    private readonly Dictionary<string, ImmutableArray<IsoValidationIssue>> validations = new(StringComparer.Ordinal);
    private IsoScene? pickingScene;
    private long pickingRevision = -1;
    private IsoPixel pickingCamera;
    /// <summary>Spec §§2, 4.2: copies all input collections and materializes legacy seats in memory.</summary>
    public IsoEditor(IsoPack pack, IsoLayout layout)
    {
        this.pack = pack; this.layout = IsoEditorDocument.Own(layout, pack); saved = this.layout;
        defaults = IsoEditorDocument.Default(pack); BuildPalette(); Remember(this.layout);
    }
    /// <summary>Spec §4.2: immutable document and history flags.</summary>
    public IsoEditorState State => new(layout, revision, undo.Count > 0, redo.Count > 0, !IsoEditorDocument.Equal(layout, saved));
    /// <summary>Spec §8: built-in and successfully merged user objects, never individual render parts.</summary>
    public ImmutableArray<IsoEditableAsset> Palette => palette;
    /// <summary>Spec §§3.2, 8: complete wall assemblies grouped from compiled edge modules.</summary>
    public ImmutableArray<IsoWallDecorDefinition> WallPalette => walls;
    /// <summary>Spec §7.4: replaces the active pack only after every current/history reference resolves.</summary>
    public IsoEditResult UpdatePack(IsoPack pack)
    {
        if (ReferenceEquals(pack, this.pack)) return Result(true, false, Validate());
        var problems = PreviewPack(pack);
        if (problems.Any(i => i.Severity == IsoIssueSeverity.Error)) return Result(false, false, problems);
        this.pack = pack; BuildPalette(); revision++; validation = default; validations.Clear(); return Result(true, false, Validate());
    }
    /// <summary>Spec §7.4: validates all documents without replacing pack, palette or revision.</summary>
    public ImmutableArray<IsoValidationIssue> PreviewPack(IsoPack pack)
    {
        var limits = IsoEditorValidation.PackLimits(pack);
        if (!limits.IsEmpty) return limits;
        var problems = new List<IsoValidationIssue>();
        foreach (var document in undo.Concat(redo).Append(layout).Append(saved).Append(defaults))
            problems.AddRange(IsoEditorValidation.Validate(document, pack).Where(IsoEditorValidation.Structural));
        if (problems.Count > 0) problems.Add(IsoEditorValidation.Issue("C006", "The edit would replace an occupied slot or lose an asset reference."));
        return IsoEditorValidation.Sort(problems);
    }
    /// <summary>Spec §6: cached draft scene uses the live camera but never live furniture or masks.</summary>
    public IsoScene GetPickingScene(IsoPixel camera)
    {
        if (pickingScene != null && pickingRevision == revision && pickingCamera == camera) return pickingScene;
        var builder = new IsoSceneBuilder(pack, layout, false);
        var scene = builder.Build([]);
        var dx = camera.X - builder.Camera.X; var dy = camera.Y - builder.Camera.Y;
        pickingScene = scene with { Items = scene.Items.Select(i => i with { X = i.X + dx, Y = i.Y + dy }).ToImmutableArray() };
        pickingRevision = revision; pickingCamera = camera; return pickingScene;
    }
    /// <summary>Spec §4.1 Place: creates one instance; a desk is undesignated until SetSeat links a chair.</summary>
    public IsoEditResult Place(string assetId, Tile origin, int rotation = 0, string state = "default",
        string? hostDeskId = null) => Execute(new IsoPlaceCommand(assetId, origin, rotation, state, hostDeskId));
    /// <summary>Spec §4.1 Move: absolute grid origin of the resolved assembly root.</summary>
    public IsoEditResult Move(string instanceId, Tile origin) => Execute(new IsoMoveCommand(instanceId, origin));
    /// <summary>Spec §4.1 Rotate: selects exact authored geometry; null cycles current-state rotations.</summary>
    public IsoEditResult Rotate(string instanceId, int? rotation = null) => Execute(new IsoRotateCommand(instanceId, rotation));
    /// <summary>Spec §4.1 Delete: removes root, links and designation in one edit.</summary>
    public IsoEditResult Delete(string instanceId) => Execute(new IsoDeleteCommand(instanceId));
    /// <summary>Spec §4.1 Duplicate: remaps all instance/seat links; never copies a runtime reservation.</summary>
    public IsoEditResult Duplicate(string instanceId, Tile? origin = null) => Execute(new IsoDuplicateCommand(instanceId, origin));
    /// <summary>Spec §§3, 4.1 SetSeat: derives contacts from IsoContacts.Desk, never from pointer pixels.</summary>
    public IsoEditResult SetSeat(string deskId, string chairId, string? computerId = null) => Execute(new IsoSetSeatCommand(deskId, chairId, computerId));
    /// <summary>Spec §4.1 ClearSeat: idempotently removes only designation.</summary>
    public IsoEditResult ClearSeat(string deskId) => Execute(new IsoClearSeatCommand(deskId));
    /// <summary>Spec §4.1 PlaceWall: mounts one complete authored decor assembly; walls are fixed.</summary>
    public IsoEditResult PlaceWall(string assetId, string hostWallId) => Execute(new IsoPlaceWallCommand(assetId, hostWallId));
    /// <summary>Spec §4.1 MoveWall: absolute primary host, same face; preserves attachment IDs.</summary>
    public IsoEditResult MoveWall(string instanceId, string hostWallId) => Execute(new IsoMoveWallCommand(instanceId, hostWallId));
    /// <summary>Spec §4.1 DuplicateWall: new assembly and edge IDs at an explicit primary host.</summary>
    public IsoEditResult DuplicateWall(string instanceId, string hostWallId) => Execute(new IsoDuplicateWallCommand(instanceId, hostWallId));
    /// <summary>Spec §4.2: restores one complete snapshot, advancing revision.</summary>
    public IsoEditResult Undo() => Restore(undo, redo, "Nothing to undo.");
    /// <summary>Spec §4.2: restores one complete snapshot, advancing revision.</summary>
    public IsoEditResult Redo() => Restore(redo, undo, "Nothing to redo.");
    /// <summary>Spec §§2.2, 4.1: restores the active bundled twelve-seat room as one undoable edit.</summary>
    public IsoEditResult ResetDefault() => Commit(defaults, IsoEditorValidation.Validate(defaults, pack), null);
    /// <summary>Spec §5: aggregate independent errors in stable rule/target order, without mutation.</summary>
    public ImmutableArray<IsoValidationIssue> Validate() => validation.IsDefault ? validation = ValidateDocument(layout) : validation;
    /// <summary>Spec §6: same candidate and issues as command dispatch, without side effects.</summary>
    public IsoEditPreview Preview(IsoEditorCommand command)
    {
        var built = Candidate(command); var markers = IsoEditorPreview.Markers(built.Layout, pack, built.Target);
        return new(built.Layout, markers.Cells, markers.Points, built.Issues, !built.Issues.Any(IsoEditorValidation.Structural));
    }
    /// <summary>Spec §2.2: validates before any write; atomic sibling temporary/backup; no undo step.</summary>
    public IsoEditResult Save(string path)
    {
        var problems = Validate();
        if (problems.Any(i => i.Severity == IsoIssueSeverity.Error))
            return Result(false, false, IsoEditorValidation.Sort(problems.Append(IsoEditorValidation.Issue("C008", "Cannot save an invalid iso layout."))));
        try
        {
            IsoEditorPersistence.Save(path, IsoEditorDocument.Bytes(layout)); saved = layout;
            return Result(true, false, problems);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { return Result(false, false, [IsoEditorValidation.Issue("C009", "Could not save the iso layout: " + PrivacyModes.Diagnostic(e, PrivacyMode.Full))]); }
    }
    /// <summary>Spec §7.4: all asset IDs used by draft, Undo and Redo, for safe catalog removal.</summary>
    public ImmutableArray<string> ReferencedAssetIds => undo.Concat(redo).Append(layout).Append(saved)
        .SelectMany(IsoEditorDocument.Assets).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    private void BuildPalette()
    {
        palette = (pack.Manifest.Furniture ?? []).OrderBy(a => a.Id, StringComparer.Ordinal).Select(a =>
        {
            var variants = a.Variants.OrderBy(v => v.State, StringComparer.Ordinal).ThenBy(v => v.Rotation).Select(IsoEditorDocument.Freeze).ToImmutableArray();
            var part = variants.First().Parts.First();
            return new IsoEditableAsset(a.Id, a.Id, a.Kind ?? "furniture", variants, part.SheetId, part.Animation ?? part.Clip!, a.Id == "iso/desk", a.Id == "iso/computer");
        }).ToImmutableArray();
        walls = IsoEditorWallCatalog.Build(pack);
    }
    private void Remember(IsoLayout document) => consumed.UnionWith(IsoEditorDocument.Ids(document));
    private IsoEditResult Result(bool succeeded, bool changed, ImmutableArray<IsoValidationIssue> issues, string? target = null) => new(succeeded, changed, State, issues, target);
    private IsoEditorCommands.Candidate Candidate(IsoEditorCommand command)
    {
        if (layout.Furniture == null || Validate().Any(i => i.RuleId == "L003")) return new(layout, Validate(), null, counter);
        var built = new IsoEditorCommands(pack, layout, new(consumed, StringComparer.Ordinal), counter).Build(command);
        var problems = IsoEditorDocument.Equal(built.Layout, layout) ? Validate() : ValidateDocument(built.Layout);
        return built with { Issues = IsoEditorValidation.Sort(built.Issues.Concat(problems)) };
    }
    private ImmutableArray<IsoValidationIssue> ValidateDocument(IsoLayout document)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(IsoEditorDocument.Bytes(document)));
        if (validations.TryGetValue(key, out var cached)) return cached;
        var issues = IsoEditorValidation.Validate(document, pack);
        if (validations.Count >= 102) validations.Remove(validations.Keys.First());
        validations.Add(key, issues); return issues;
    }
    private IsoEditResult Execute(IsoEditorCommand command)
    {
        var built = Candidate(command);
        if (built.Issues.Any(IsoEditorValidation.Structural)) return Result(false, false, built.Issues);
        var result = Commit(built.Layout, built.Issues, built.Target);
        if (result.Changed) counter = built.Counter;
        return result;
    }
    private IsoEditResult Commit(IsoLayout document, ImmutableArray<IsoValidationIssue> problems, string? target)
    {
        if (IsoEditorDocument.Equal(document, layout)) return Result(true, false, problems, target);
        undo.Add(layout); if (undo.Count > 100) undo.RemoveAt(0); redo.Clear();
        layout = document; Remember(layout); revision++; validation = problems;
        return Result(true, true, problems, target);
    }
    private IsoEditResult Restore(List<IsoLayout> source, List<IsoLayout> destination, string message)
    {
        if (source.Count == 0) return Result(false, false, [IsoEditorValidation.Issue("C007", message)]);
        destination.Add(layout); layout = source[^1]; source.RemoveAt(source.Count - 1);
        revision++; validation = default; return Result(true, true, Validate());
    }
}
