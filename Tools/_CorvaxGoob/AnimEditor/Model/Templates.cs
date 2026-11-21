using YamlDotNet.RepresentationModel;

namespace Abisfera.AnimEditor.Model;

/// <summary>Starting points for new scenes.</summary>
public static class Templates
{
    /// <summary>Folder (under Resources/Prototypes) where scenes made in the editor go.</summary>
    public const string EditorFolder = "_CorvaxGoob/ProcAnim/Editor";

    // Two actors a tile apart: the attacker swings, the victim is knocked down. Every bone the editor shows has a track
    // so it can be posed straight away.
    private const string Finisher = """
- type: procAnimScene
  id: {ID}
  category: Finisher
  editorName: {NAME}
  length: 1.6
  requiredRoom: 1.2
  actors:
  - role: attacker
    facing: 1
    look: {keys: [{t: 0, x: 1}]}
    move:
    - {t: 0, x: 0}
    - {t: 0.4, x: 0.15, ease: In}
    - {t: 1.4, x: 0}
    parts:
      Body:
        pivot: 0, -14
        keys: [{t: 0, rot: 0}, {t: 0.25, rot: 6, ease: Out}, {t: 0.4, rot: -10, ease: In}, {t: 1.4, rot: 0}]
      Head:
        pivot: 0, 6
        keys: [{t: 0, rot: 0}, {t: 1.4, rot: 0}]
      ArmNear:
        pivot: 0, 5
        keys: [{t: 0, rot: 10}, {t: 0.25, rot: -40, ease: Out}, {t: 0.4, rot: 80, ease: In}, {t: 1.4, rot: 10}]
      ArmNearLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 30}, {t: 0.25, rot: 100, ease: Out}, {t: 0.4, rot: 5, ease: In}, {t: 1.4, rot: 30}]
      ArmFar:
        pivot: 0, 5
        keys: [{t: 0, rot: -10}, {t: 1.4, rot: -10}]
      ArmFarLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 20}, {t: 1.4, rot: 20}]
      LegNear:
        pivot: 0, -8
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 14, ease: In}, {t: 1.4, rot: 0}]
      LegNearLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 1.4, rot: 0}]
      LegFar:
        pivot: 0, -8
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: -10, ease: In}, {t: 1.4, rot: 0}]
      LegFarLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 1.4, rot: 0}]
  - role: victim
    facing: -1
    standUp: false
    move:
    - {t: 0, x: 0.8}
    - {t: 0.4, x: 0.8}
    - {t: 0.8, x: 1.1, ease: Out}
    parts:
      Body:
        pivot: 0, -14
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.8, x: 0, y: -8, rot: -90, ease: Out}, {t: 1.6, x: 0, y: -8, rot: -90}]
      Head:
        pivot: 0, 6
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.5, rot: -25, ease: HitStop}, {t: 1, rot: 0}]
      ArmNear:
        pivot: 0, 5
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.8, rot: -60, ease: Out}]
      ArmNearLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: 20}]
      ArmFar:
        pivot: 0, 5
        keys: [{t: 0, rot: 0}, {t: 0.4, rot: 0}, {t: 0.8, rot: -80, ease: Out}]
      ArmFarLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: 10}]
      LegNear:
        pivot: 0, -8
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: 20}]
      LegNearLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: 0}]
      LegFar:
        pivot: 0, -8
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: -10}]
      LegFarLow:
        pivot: 0, 0
        keys: [{t: 0, rot: 0}, {t: 0.8, rot: 0}]
  events:
  - {t: 0.4, target: victim, beat: hit, sheet: true, hitStop: 0.06, shake: 0.6, flash: true,
     sound: {collection: Punch}, blood: {count: 6}}
""";

    public static YamlMappingNode NewFinisher(string id, string name)
    {
        var text = Finisher.Replace("{ID}", id).Replace("{NAME}", YamlText.Emit(YamlText.S(name), 0, true, "\n"));
        var node = SceneDocument.FindScene(YamlText.Parse(text), id);
        return node ?? throw new InvalidDataException("шаблон не разобрался");
    }

    public static bool ValidId(string id)
        => id.Length > 0 && char.IsLetter(id[0]) && id.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
}
