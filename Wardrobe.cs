using System;
using System.Collections.Generic;
using System.Linq;
using SimpleJSON;
using UnityEngine;

namespace SmittyWerben
{
    /**
 * Outfit manager.
 *
 * Apply outfits to clothing pieces.
 *
 * Authors: VamDazzler
 * License: Creative Commons with Attribution (CC BY 3.0)
 */
    public class Wardrobe : MVRScript
    {
        private bool _disableUpdate;

        //person script is attatched too
        JSONStorableStringChooser _clothingItems, _outfitNames;
        JSONStorableString _materialList;
        UIDynamicButton _applyButton, _dumpButton, _forceReloadButton;
        StorableReplacements _storedOutfits;

        Dictionary<KeyValuePair<Material, string>, Texture> _originalTextures =
            new Dictionary<KeyValuePair<Material, string>, Texture>();

        // Indicate whether loading from the JSON has completed.
        // Initial load of textures must wait until the clothes have all been loaded,
        // which is not the case by the time of `Start` on a fresh start of VaM.
        private bool _needsLoad;

        private VdTextureLoader _textureLoader = new VdTextureLoader();

        public override void Init()
        {
            try
            {
                _disableUpdate = true;
                pluginLabelJSON.val = "Wardrobe v2.2.2 (by VamDazzler)";

                // Obtain our person
                if (containingAtom == null)
                {
                    SuperController.LogError("Please add this plugin to a PERSON atom.");
                    throw new Exception("Halting Wardrobe due to de-Atom-ization");
                }

                // Create the clothing items drop-down
                _clothingItems = new JSONStorableStringChooser("clothing", _emptyChoices, null, "Clothing Item");
                UIDynamicPopup clothingSelector = CreateScrollablePopup(_clothingItems);

                // Create the outfit selection drop-down
                _outfitNames = new JSONStorableStringChooser("outfit", _emptyChoices, null, "Outfit");
                UIDynamicPopup outfitSelector = CreateScrollablePopup(_outfitNames);
                outfitSelector.popupPanelHeight = 900f;
                RectTransform panel = outfitSelector.popup.popupPanel;
                panel.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 400f);
                panel.pivot = new Vector2(0.35f, 1.0f);

                // Create the slot in which all changed textures are stored.
                _storedOutfits = new StorableReplacements();
                RegisterString(_storedOutfits);

                // Action to perform replacement
                _applyButton = CreateButton("Apply");
                _applyButton.button.onClick.AddListener(ApplyOutfitCallback);

                // Force reload
                _forceReloadButton = CreateButton("Refresh Textures");
                _forceReloadButton.button.onClick.AddListener(ForceReloadCallback);

                // Create a dump button
                UIDynamic align = CreateSpacer(true);
                align.height = 25;
                _dumpButton = CreateButton("Dump OBJ and MTL files - look in root", true);
                if (_dumpButton != null)
                {
                    _dumpButton.button.onClick.AddListener(DumpButtonCallback);
                    _dumpButton.button.interactable = false;
                }

                // Create the material listing window
                _materialList = new JSONStorableString("matlist", "");
                UIDynamicTextField matListTextField = CreateTextField(_materialList, true);
                matListTextField.height = 400f;
            }
            catch (Exception ex)
            {
                SuperController.LogError($"Could not initialize Wardrobe {ex}");
            }
        }

        public void Update()
        {
            try
            {
                if (_needsLoad && !SuperController.singleton.isLoading)
                {
                    // Load all the previously saved replacements
                    foreach (var entry in _storedOutfits.All())
                    {
                        try
                        {
                            ApplyOutfit(entry.Key, entry.Value);
                        }
                        catch (Exception ex)
                        {
                            SuperController.LogError($"Could not load outfit '{entry.Value}' for {entry.Key}: {ex}");
                        }
                    }

                    // Reset the UI (cascades)
                    SelectClothingItem(null);

                    // Allow updates to occur normally.
                    _disableUpdate = false;
                    _needsLoad = false;
                }
            }
            catch (Exception ex)
            {
                if (!_disableUpdate)
                {
                    SuperController.LogError("Error while updating " + ex);
                    _disableUpdate = true;
                }
            }
        }

        void Start()
        {
            try
            {
                // No point if we don't have a person.
                if (containingAtom == null)
                    return;

                // Now that loading is complete, set our UI callbacks
                _clothingItems.setCallbackFunction = this.SelectClothingItem;
                _outfitNames.setCallbackFunction = this.SelectOutfit;
                SelectClothingItem(null);

                _needsLoad = true;
            }
            catch (Exception ex)
            {
                SuperController.LogError($"Could not start Wardrobe {ex}");
                _disableUpdate = true;
            }
        }

        //
        // UI action callbacks

        private void SelectClothingItem(string clothingName)
        {
            SelectOutfit(null);
            if (clothingName == null)
            {
                List<string> clothings = GameObject
                    .FindObjectsOfType<DAZClothingItem>()
                    .Where(dci => dci.containingAtom == containingAtom)
                    .Select(dci => dci.name)
                    .ToList();
                clothings.Insert(0, "REFRESH");
                _clothingItems.choices = clothings;

                // No clothing selected, disable dumping OBJs and reloading textures.
                _dumpButton.button.interactable = false;
                _forceReloadButton.button.interactable = false;

                // Update the material list to show nothing
                _materialList.val = "(material list, select clothes)";
            }
            else if (clothingName == "REFRESH")
            {
                // call us again with no value.
                _clothingItems.val = null;
            }
            else
            {
                // Turn on the OBJ dump and forced texture reload
                _dumpButton.button.interactable = true;
                _forceReloadButton.button.interactable = true;

                // Create the list of materials.
                string matlist = GameObject
                    .FindObjectsOfType<DAZClothingItem>()
                    .Where(dci => dci.containingAtom == containingAtom)
                    .Where(dci => dci.name == clothingName)
                    .First()
                    .GetComponentsInChildren<DAZSkinWrap>()
                    .First()
                    .GPUmaterials
                    .Select(mat => mat.name)
                    .Aggregate((l, r) => l.Length > 0 && r.Length > 0 ? $"{l}\n{r}" : $"{l}{r}");
                _materialList.val = matlist;

                // Get a list of outfits
                List<string> outfits = FindOutfits(clothingName).ToList();
                _outfitNames.choices = outfits;

                if (outfits.Count == 1)
                {
                    // Pre-select the single outfit.
                    _outfitNames.val = outfits.ElementAt(0);
                }
            }
        }

        private void SelectOutfit(string outfitName)
        {
            if (outfitName == null)
            {
                _outfitNames.choices = _emptyChoices;
                _outfitNames.valNoCallback = null;
                _applyButton.button.interactable = false;
            }
            else
            {
                _applyButton.button.interactable = true;
            }
        }

        public void DumpButtonCallback()
        {
            try
            {
                // Obtain the currently selected clothes.
                DAZClothingItem clothes = GameObject
                    .FindObjectsOfType<DAZClothingItem>()
                    .Where(dci => dci.containingAtom == containingAtom)
                    .Where(dci => dci.name == _clothingItems.val)
                    .DefaultIfEmpty((DAZClothingItem)null)
                    .FirstOrDefault();

                // Bug out if it doesn't exist.
                if (clothes == null)
                {
                    SuperController.LogError($"Could not finding clothing item '{_clothingItems.val}'");
                    return;
                }

                // Get the first skinwrap mesh.
                DAZMesh mesh = clothes
                    .GetComponentsInChildren<DAZMesh>()
                    .FirstOrDefault();

                // Export
                OBJExporter exporter =gameObject.AddComponent<OBJExporter>();
                var enabledMaterials = new Dictionary<int, bool>();

                for (int i = 0; i < mesh.materialsEnabled.Length; i++)
                {
                    enabledMaterials[i] = mesh.materialsEnabled[i];
                }

                exporter.Export(
                exportPath:    clothes.name + ".obj",
                    mesh: mesh.uvMappedMesh,
                    vertices: mesh.uvMappedMesh.vertices,
                    normals: mesh.uvMappedMesh.normals,
                    mats: mesh.materials,
                    enabledMats:enabledMaterials
                );
            }
            catch (Exception ex)
            {
                SuperController.LogMessage($"Could not export OBJ file for this clothing item: {ex}");
            }
        }

        private void ApplyOutfitCallback()
        {
            try
            {
                if (_clothingItems.val != null && _outfitNames.val != null)
                    ApplyOutfit(_clothingItems.val, _outfitNames.val);
                _storedOutfits.SetOutfit(_clothingItems.val, _outfitNames.val);
            }
            catch (Exception ex)
            {
                SuperController.LogError("Could not apply outfit: " + ex);
            }
        }

        private void ForceReloadCallback()
        {
            try
            {
                string outfitName = _storedOutfits.GetOutfit(_clothingItems.val);
                string outfitDirectory = FindOutfitDirectory(_clothingItems.val, outfitName);

                // Expire the textures in the outfit's directory
                _textureLoader.ExpireDirectory(outfitDirectory);

                if (outfitName != null)
                {
                    ApplyOutfit(_clothingItems.val, _outfitNames.val);
                }
            }
            catch (Exception ex)
            {
                SuperController.LogError("Could not reload textures: " + ex);
            }
        }

        //
        // Outfit application methods

        private void ApplyOutfit(string forClothing, string outfitName)
        {
            string outfitDirectory = FindOutfitDirectory(forClothing, outfitName);

            // Get the clothing item materials.
            DAZClothingItem clothes = GameObject
                .FindObjectsOfType<DAZClothingItem>()
                .Where(dci => dci.containingAtom == containingAtom)
                .Where(dci => dci.name == forClothing)
                .FirstOrDefault();
            if (clothes == null)
                throw new Exception(
                    "Tried to apply '{outfitName}' to '{forClothing}' but '{myPerson.name}' isn't wearing that.");

            string[] files = SuperController.singleton.GetFilesAtPath(outfitDirectory);

            foreach (Material mat in clothes
                         .GetComponentsInChildren<DAZSkinWrap>()
                         .SelectMany(dsw => dsw.GPUmaterials))
            {
                ApplyTexture(outfitDirectory, mat, _propDiffuse, VdTextureLoader.TypeDiffuse);
                ApplyTexture(outfitDirectory, mat, _propCutout, VdTextureLoader.TypeDiffuse);
                ApplyTexture(outfitDirectory, mat, _propNormal, VdTextureLoader.TypeNormal);
                ApplyTexture(outfitDirectory, mat, _propSpec, VdTextureLoader.TypeSpecular);
                ApplyTexture(outfitDirectory, mat, _propGloss, VdTextureLoader.TypeGloss);
            }
        }

        private void ExpireOutfitTextures(string forClothing, string outfitName)
        {
            string outfitDirectory = FindOutfitDirectory(forClothing, outfitName);
            _textureLoader.ExpireDirectory(outfitDirectory);

            ApplyOutfit(forClothing, outfitName);
        }

        private void ApplyTexture(string outfitDirectory, Material mat, string property, int ttype)
        {
            string textureFilename = TEXNames(mat, property)
                .SelectMany(tn => SuperController.singleton.GetFilesAtPath(outfitDirectory, $"{tn}.*"))
                .DefaultIfEmpty((string)null)
                .FirstOrDefault();

            var key = new KeyValuePair<Material, string>(mat, property);
            if (textureFilename != null)
            {
                // Save the original texture if we haven't already.
                if (!_originalTextures.ContainsKey(key))
                    _originalTextures[key] = mat.GetTexture(property);

                _textureLoader.WithTexture(textureFilename, ttype, tex => mat.SetTexture(property, tex));
            }
            else
            {
                if (_originalTextures.ContainsKey(key))
                    mat.SetTexture(property, _originalTextures[key]);
            }
        }

        private static IEnumerable<string> DiffuseTexNames(Material mat)
        {
            if (mat.HasProperty(_propDiffuse))
            {
                bool hasAlpha = mat.HasProperty(_propCutout);

                yield return $"{mat.name}D";
                if (hasAlpha) yield return $"{mat.name}";
                yield return "defaultD";
                if (hasAlpha) yield return "default";
            }
        }

        private static IEnumerable<string> AlphaTexNames(Material mat)
        {
            if (mat.HasProperty(_propCutout))
            {
                bool hasDiffuse = mat.HasProperty(_propDiffuse);

                yield return $"{mat.name}A";
                if (hasDiffuse) yield return $"{mat.name}";
                yield return $"defaultA";
                if (hasDiffuse) yield return $"default";
            }
        }

        private static IEnumerable<string> OtherTexNames(Material mat, string propName, string suffix)
        {
            if (mat.HasProperty(propName))
            {
                yield return $"{mat.name}{suffix}";
                yield return $"default{suffix}";
            }
        }

        private static IEnumerable<string> TEXNames(Material mat, string propName)
        {
            switch (propName)
            {
                case _propDiffuse:
                    return DiffuseTexNames(mat);
                case _propCutout:
                    return AlphaTexNames(mat);
                case _propGloss:
                    return OtherTexNames(mat, _propGloss, "G");
                case _propNormal:
                    return OtherTexNames(mat, _propNormal, "N");
                case _propSpec:
                    return OtherTexNames(mat, _propSpec, "S");

                default:
                    throw new Exception($"Unknown shader property '{propName}'");
            }
        }

        //
        // Helper classes and utility methods

        private IEnumerable<string> FindOutfits(string forClothing)
        {
            string localDirectory = $"{SuperController.singleton.currentLoadDir}/Textures/Wardrobe/{forClothing}";
            string globalDirectory = $"{SuperController.singleton.savesDir}/../Textures/Wardrobe/{forClothing}";

            // Collect outfit directories from both the scene and global levels.
            return SafeGetDirectories(localDirectory).Union(SafeGetDirectories(globalDirectory))
                .Select(GetBaseName)
                .Where(bn => bn.ToLower() != "psd")
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        private string FindOutfitDirectory(string forClothing, string outfitName)
        {
            string sceneDirectory = $"{SuperController.singleton.currentLoadDir}/Textures/Wardrobe/{forClothing}";
            string globalDirectory = $"{SuperController.singleton.savesDir}/../Textures/Wardrobe/{forClothing}";

            string outfitDirectory = SafeGetDirectories(sceneDirectory)
                .Union(SafeGetDirectories(globalDirectory))
                .Where(dir => GetBaseName(dir).ToLower() == outfitName.ToLower())
                .DefaultIfEmpty((string)null)
                .FirstOrDefault();

            if (outfitDirectory == null)
            {
                throw new Exception(
                    "Outfit needs textures in '<vamOrScene>/Textures/Wardrobe/{forClothing}/{outfitName}'");
            }

            return outfitDirectory;
        }

        private class StorableReplacements : JSONStorableString
        {
            private Dictionary<string, string> _entries = new Dictionary<string, string>();

            public StorableReplacements() : base("replacements", "<placeholder>")
            {
            }

            public void SetOutfit(string clothingName, string outfitName)
            {
                _entries[clothingName] = outfitName;
            }

            public string GetOutfit(string clothingName)
            {
                return _entries[clothingName];
            }

            public bool IsOutfitted(string clothingName)
            {
                return _entries.ContainsKey(clothingName);
            }

            public IEnumerable<KeyValuePair<string, string>> All()
            {
                return _entries;
            }

            public  void LateRestoreFromJSON(JSONClass jc, bool restorePhysical = true,
                bool restoreAppearance = true)
            {
                // This may not be necessary, I don't know the lifecycle of a JSONStorable well enough.
                RestoreFromJSON(jc, restorePhysical, restoreAppearance);
            }

            public override bool NeedsLateRestore(JSONClass jc, bool restorePhysical = true,
                bool restoreAppearance = true)
            {
                return false;
            }

            public override bool NeedsRestore(JSONClass jc, bool restorePhysical = true, bool restoreAppearance = true)
            {
                return true;
            }

            public  void RestoreFromJSON(JSONClass jc, bool restorePhysical = true,
                bool restoreAppearance = true)
            {
                _entries = new Dictionary<string, string>();
                if (!jc.Keys.Contains("version") || jc["version"].AsInt != 4)
                {
                    // this is version 1, the undocumented
                    SuperController.LogError("Cannot load Wardrobe v1 save. Everything has changed, sorry.");
                }
                else
                {
                    // Assume the most recent version.
                    ParseReplacements(jc["replacements"] as JSONArray);
                }
            }

            private void ParseReplacements(JSONArray replacements)
            {
                foreach (JSONClass obj in replacements)
                {
                    _entries[obj["clothes"]] = obj["outfit"];
                }
            }

            public override bool StoreJSON(JSONClass jc, bool includePhysical = true, bool includeAppearance = true,
                bool forceStore = false)
            {
                var replacements = new JSONArray();
                foreach (var kvp in _entries)
                {
                    JSONClass obj = new JSONClass();
                    obj["clothes"] = kvp.Key;
                    obj["outfit"] = kvp.Value;
                    replacements.Add(obj);
                }

                jc.Add("version", new JSONData(4));
                jc.Add("replacements", replacements);
                return true;
            }
        }

        private static string[] SafeGetDirectories(string inDir)
        {
            try
            {
                return SuperController.singleton.GetDirectoriesAtPath(inDir);
            }
            catch
            {
                return new string[0];
            }
        }

        // Get the basename (last part of a path, usually filename) from a fully qualified filename.
        private static string GetBaseName(string fqfn)
        {
            string[] comps = fqfn.Split('\\', '/');
            return comps[comps.Length - 1];
        }

        private static string RemoveExt(string fn)
        {
            return fn.Substring(0, fn.LastIndexOf('.'));
        }

        private static string OnlyExt(string fn)
        {
            return fn.Substring(fn.LastIndexOf('.') + 1);
        }

        private static readonly List<string> _emptyChoices = new List<string>();
        private const string _propDiffuse = "_MainTex";
        private const string _propCutout = "_AlphaTex";
        private const string _propNormal = "_BumpMap";
        private const string _propGloss = "_GlossTex";
        private const string _propSpec = "_SpecTex";
    }
}
