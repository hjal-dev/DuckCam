using System;
using System.Collections.Generic;
using System.Linq;
using DuckCam.Actors;
using DuckCam.Animation;
using DuckCam.Core;
using UnityEngine;

namespace DuckCam.UI
{
    internal sealed partial class DuckCamUI
    {
        private bool _picking;

        private int _spawnAt;

        private bool _hideOriginal = true;

        private string _actorMessage;

        private void DrawCharacters()
        {
            if (_kit.Action("Spawn Bots", "Open Spawner"))
            {
                Spawner.Show();
            }
            if (_kit.Action("Add Any Bot (photo only)", "Open Spawner"))
            {
                Spawner.Show();
            }
            _kit.Note("Spawn real bots (any faction, bosses and modded factions included) with your own gear, weapons, presets and saved kits. Or build one there and press ADD TO PHOTO: it becomes a posable character only (no AI, not in the raid) that you can pose, move and re-dress here like any other character. From a live scene, the scene freezes when you place it.");

            _kit.Section("Weapons in hands");
            _c.Weapons.HidePmc = _kit.Toggle("Hide My PMC's Weapon", _c.Weapons.HidePmc);
            _c.Weapons.HideBots = _kit.Toggle("Hide Bots' Weapons", _c.Weapons.HideBots);
            int photoWeapons = _kit.Buttons("Photo Characters: Hide All", "Show All");
            if (photoWeapons >= 0)
            {
                foreach (PhotoActor a in _c.Actors.Actors.Where(a => a.Status == ActorStatus.Ready))
                {
                    a.SetWeaponVisible(photoWeapons == 1);
                }
            }
            _kit.Note("Hides only the weapon held in the hands (slung and holstered gear stays). Works live and frozen, catches weapon switches and new bots, and everything comes back when you turn it off or leave DuckCam. Photo characters also have their own Weapon In Hands switch below.");
            ActorManager actors = _c.Actors;
            if (!_c.Frozen)
            {
                _kit.Value("Characters", "Freeze scene first");
                if (_kit.Action("Freeze Raid", "Freeze"))
                {
                    _c.SetFrozen(true);
                }
                _kit.Note("Characters are posable copies (your PMC, players in the raid, or saved appearances) with the Hideout Gear Rack poses. They need a frozen scene and are removed when it goes live.");
                return;
            }

            var slots = new List<HudKit.Slot>();
            for (int i = 0; i < actors.Actors.Count; i++)
            {
                slots.Add(new HudKit.Slot { Text = (i + 1).ToString() });
            }
            if (actors.CanAdd)
            {
                slots.Add(new HudKit.Slot { IsAdd = true });
            }
            int picked = _kit.Slots("Characters", slots, _picking ? actors.Actors.Count : actors.Selected);
            if (picked >= 0)
            {
                if (picked >= actors.Actors.Count)
                {
                    _picking = true;
                }
                else
                {
                    _picking = false;
                    actors.Selected = picked;
                }
                _kit.ClearBuffers();
            }

            if (_picking || actors.Actors.Count == 0)
            {
                DrawActorPicker(actors);
                return;
            }

            bool showNpcs = _kit.Toggle("Surrounding NPCs", !actors.SurroundingHidden);
            if (showNpcs == actors.SurroundingHidden)
            {
                actors.SetSurroundingHidden(!showNpcs, _c.Player, _c.Restores);
            }

            string[] names = actors.Actors.Select(a => a.Name).ToArray();
            actors.Selected = _kit.Selector("Edit Character", names, Mathf.Clamp(actors.Selected, 0, names.Length - 1));
            PhotoActor actor = actors.SelectedActor;
            if (actor == null)
            {
                return;
            }

            switch (actor.Status)
            {
                case ActorStatus.Loading:
                    _kit.Value("Status", "Loading model...");
                    return;
                case ActorStatus.Failed:
                    _kit.Value("Status", "Unavailable");
                    _kit.Note(actor.Error);
                    if (_kit.Action("Remove", "Remove"))
                    {
                        actors.Remove(actor);
                    }
                    return;
                case ActorStatus.None:
                    _kit.Value("Status", "Removed");
                    return;
            }

            DrawActorPose(actor);

            float yaw = _kit.Slider("Rotate", actor.Yaw, -180f, 180f, 2f, "0");
            ViewAxes(out Vector3 viewRight, out Vector3 viewFar);
            Vector3 offsetWorld = actor.SpawnRotation * new Vector3(actor.OffsetRight, 0f, actor.OffsetForward);
            float shownRight = Vector3.Dot(offsetWorld, viewRight);
            float shownFar = Vector3.Dot(offsetWorld, viewFar);
            float newRight = _kit.Slider("Left / Right", shownRight, Mathf.Min(-25f, shownRight - 5f), Mathf.Max(25f, shownRight + 5f), 0.05f, "0.00", " m");
            float newFar = _kit.Slider("Close / Far", shownFar, Mathf.Min(-25f, shownFar - 5f), Mathf.Max(25f, shownFar + 5f), 0.05f, "0.00", " m");
            float right = actor.OffsetRight;
            float forward = actor.OffsetForward;
            if (newRight != shownRight || newFar != shownFar)
            {
                Vector3 moved = offsetWorld + viewRight * (newRight - shownRight) + viewFar * (newFar - shownFar);
                Vector3 local = Quaternion.Inverse(actor.SpawnRotation) * moved;
                right = local.x;
                forward = local.z;
            }
            float up = _kit.Slider("Up / Down", actor.OffsetUp, Mathf.Min(-10f, actor.OffsetUp - 2f), Mathf.Max(10f, actor.OffsetUp + 2f), 0.02f, "0.00", " m");
            bool placed = _kit.Action("Move To Where Camera Looks", "Place");
            if (placed)
            {
                Vector3 spot = ScreenCentrePoint();
                Vector3 local = Quaternion.Inverse(actor.SpawnRotation) * (spot - actor.SpawnPosition);
                right = local.x;
                forward = local.z;
                up = spot.y - actor.SpawnPosition.y;
            }
            if (placed || yaw != actor.Yaw || right != actor.OffsetRight || forward != actor.OffsetForward || up != actor.OffsetUp)
            {
                actor.Yaw = yaw;
                actor.OffsetRight = right;
                actor.OffsetForward = forward;
                actor.OffsetUp = up;
                actor.ApplyTransform();
            }
            if (_kit.Action("Pose Bone By Bone", "Pose Editor"))
            {
                PoseEditor.Show(actor);
            }
            bool eyes = _c.Camera.ActorSubject == actor;
            if (_kit.Action("See Through Their Eyes", eyes ? "Stop" : "First Person"))
            {
                if (eyes)
                {
                    _c.Camera.StopSpectating();
                }
                else
                {
                    _c.Camera.ViewThroughActor(actor);
                    _c.LastMessage = "First person from " + actor.Name + ". Hold the right mouse button to look; pose them in the pose editor (Ctrl+P) and the view follows the head.";
                }
            }
            if (_kit.Action("Animate Poses Over Time", Timeline.Open ? "Close Timeline" : "Timeline"))
            {
                Timeline.Toggle();
            }
            DrawFineControls(actor, actors);
            bool weapon = _kit.Toggle("Weapon In Hands", actor.WeaponVisible);
            if (weapon != actor.WeaponVisible)
            {
                actor.SetWeaponVisible(weapon);
            }
            DrawHandWeapon(actor);
            DrawDualWield(actor);
            actor.Visible = _kit.Toggle("Character Visible", actor.Visible);
            if (_kit.Action("Clothes & Gear", "Edit"))
            {
                int outfit = Tabs.FindIndex(t => t.Icon == "outfit");
                if (outfit >= 0)
                {
                    SelectTab(outfit);
                }
            }

            int b = _kit.Buttons("Save", "Reset Offsets", "Remove");
            if (b == 0)
            {
                string error = SavedActors.Save(actor);
                _actorMessage = error == null ? $"Saved '{actor.Name}'." : "Save failed: " + error;
            }
            else if (b == 1)
            {
                actor.Yaw = actor.OffsetRight = actor.OffsetForward = actor.OffsetUp = 0f;
                actor.ApplyTransform();
            }
            else if (b == 2)
            {
                actors.Remove(actor);
                _kit.ClearBuffers();
            }
            if (_actorMessage != null)
            {
                _kit.Note(_actorMessage);
            }
        }

        private bool _fineControls;

        private int _factionFilter;

        private void DrawFineControls(PhotoActor actor, ActorManager actors)
        {
            ActorRig rig = actor.Rig;
            if (rig == null)
            {
                return;
            }
            RigSettings s = actor.Pose;
            if (rig.HasHead)
            {
                s.Look = _kit.Enum("Look At", s.Look, new[] { "Off", "Camera", "Character", "Point" });
                if (s.Look == LookMode.Character)
                {
                    List<PhotoActor> others = actors.Actors.Where(o => o != actor && o.Status == ActorStatus.Ready).ToList();
                    if (others.Count == 0)
                    {
                        _kit.Value("  Look Target", "No other characters");
                    }
                    else
                    {
                        int current = Mathf.Max(0, others.IndexOf(s.LookActor));
                        s.LookActor = others[_kit.Selector("  Look Target", others.Select(o => o.Name).ToArray(), current)];
                    }
                }
                else if (s.Look == LookMode.Point && _kit.Action("  Look Point", s.LookPoint.HasValue ? "Re-aim" : "Set at screen centre"))
                {
                    s.LookPoint = ScreenCentrePoint();
                }
            }

            _fineControls = _kit.Toggle("Fine Pose Controls", _fineControls);
            if (!_fineControls)
            {
                return;
            }
            if (rig.HasHead)
            {
                _kit.Section("Head");
                s.HeadTurn = _kit.Slider("Head Turn", s.HeadTurn, -80f, 80f, 1f, "0", "°");
                s.HeadNod = _kit.Slider("Head Nod", s.HeadNod, -50f, 50f, 1f, "0", "°");
                s.HeadTilt = _kit.Slider("Head Tilt", s.HeadTilt, -40f, 40f, 1f, "0", "°");
                s.LookClamp = _kit.Slider("Look Range", s.LookClamp, 10f, 110f, 5f, "0", "°");
            }
            if (rig.HasSpine)
            {
                _kit.Section("Body aim");
                s.BodyAim = _kit.Toggle("Turn Body To Look Target", s.BodyAim);
                if (s.BodyAim)
                {
                    s.BodyAimWeight = _kit.Slider("  Strength", s.BodyAimWeight, 0f, 1f, 0.05f);
                    if (s.Look == LookMode.Off)
                    {
                        _kit.Note("Pick a Look At target above; the body turns toward the same target (like aiming at it).");
                    }
                }
            }
            if (_kit.Action("Bones, Hands And Feet (IK)", "Pose Editor"))
            {
                PoseEditor.Show(actor);
            }
            if (_kit.Action("Fine Controls", "Reset"))
            {
                s.HeadTurn = s.HeadNod = s.HeadTilt = 0f;
                s.LookClamp = 70f;
                s.BodyAim = false;
            }
        }

        private void ViewAxes(out Vector3 right, out Vector3 far)
        {
            Transform cam = _c.Camera.Camera != null ? _c.Camera.Camera.transform : null;
            Vector3 f = cam != null ? Vector3.ProjectOnPlane(cam.forward, Vector3.up) : Vector3.forward;
            if (f.sqrMagnitude < 0.01f && cam != null)
            {
                f = Vector3.ProjectOnPlane(cam.up, Vector3.up);
            }
            far = f.sqrMagnitude > 1e-6f ? f.normalized : Vector3.forward;
            right = Vector3.Cross(Vector3.up, far);
        }

        private Vector3 ScreenCentrePoint()
        {
            Transform cam = _c.Camera.Camera != null ? _c.Camera.Camera.transform : null;
            if (cam == null)
            {
                return Vector3.zero;
            }
            return Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 50f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point
                : cam.position + cam.forward * 1.5f;
        }

        private void DrawActorPicker(ActorManager actors)
        {
            _kit.Section($"Add character · {actors.Actors.Count}/{actors.Limit}");
            if (!actors.CanAdd)
            {
                _kit.Note($"Actor limit ({actors.Limit}) reached. Remove a character or raise the limit in the config.");
            }
            if (actors.Pmc == null && actors.CanAdd && _kit.Action("Your PMC", "Add"))
            {
                _c.AddPmcActor();
                _picking = false;
            }
            _spawnAt = _kit.Selector("Spawn At", new[] { "Original Position", "In Front Of Camera" }, _spawnAt);
            if (_spawnAt == 0)
            {
                _hideOriginal = _kit.Toggle("Hide Original", _hideOriginal);
            }

            _kit.Section("Players in this raid (nearest first)");
            Vector3 from = _c.Camera.Position;
            List<EFT.Player> candidates = ActorManager.Candidates(from, 60).Where(p => p != _c.Player).ToList();
            var factions = new List<string> { "All" };
            factions.AddRange(candidates.Select(ActorManager.Faction).Distinct().OrderBy(f => f.StartsWith("Modded", StringComparison.Ordinal) ? 1 : 0).ThenBy(f => f));
            _factionFilter = Mathf.Clamp(_factionFilter, 0, factions.Count - 1);
            _factionFilter = _kit.Selector("Faction", factions.ToArray(), _factionFilter);
            string faction = factions[_factionFilter];
            foreach (EFT.Player p in candidates.Where(p => faction == "All" || ActorManager.Faction(p) == faction).Take(20))
            {
                float distance = Vector3.Distance(from, p.Transform.position);
                if (_kit.ListRow(ActorManager.Describe(p), $"{ActorManager.Faction(p)} · {ActorManager.RoleText(p)} · {distance:0} m", "Add") == 0 && actors.CanAdd)
                {
                    _c.AddActorCopy(p, _spawnAt == 1, _hideOriginal);
                    _picking = false;
                }
            }

            string[] saved = SavedActors.List();
            if (saved.Length > 0)
            {
                _kit.Section("Saved characters");
                foreach (string name in saved)
                {
                    int action = _kit.ListRow(name, "", "Add", "Del");
                    if (action == 0 && actors.CanAdd)
                    {
                        _c.AddSavedActor(name);
                        _picking = false;
                    }
                    else if (action == 1)
                    {
                        SavedActors.Delete(name);
                    }
                }
            }
            if (actors.Actors.Count > 0 && _kit.Action("Back", "Back"))
            {
                _picking = false;
            }
            _kit.Note("Copies use the native EFT appearance pipeline with a cloned visual loadout: no AI, no damage, nothing written to any profile. Modded factions (roles the game itself does not define) are detected automatically and listed under \"Modded\" once they spawn in the raid.");
        }

        private string _savePoseName = "";

        private void DrawSavedPoses(PhotoActor a)
        {
            if (a.Animate)
            {
                a.SetAnimate(false);
            }
            _savePoseName = _kit.Text("Save As", _savePoseName);
            if (_kit.Action("Save Current Pose", "Save"))
            {
                string name = string.IsNullOrWhiteSpace(_savePoseName) ? PoseLibrary.NextDefaultName() : PoseLibrary.Clean(_savePoseName);
                string unique = name;
                for (int n = 2; PoseLibrary.List().Contains(unique, StringComparer.OrdinalIgnoreCase); n++)
                {
                    unique = $"{name} ({n})";
                }
                PoseLibrary.Save(a.Rig.Capture(unique));
                PoseLibrary.Refresh();
                _savePoseName = "";
                _poseError = null;
                _c.LastMessage = $"Pose saved as '{unique}'.";
            }

            string[] saved = PoseLibrary.List();
            if (saved.Length == 0)
            {
                _kit.Value("Pose", "None saved yet");
                _kit.Note("Pose a character (Ctrl+P opens the pose editor), then press Save Current Pose here or SAVE NEW in the pose editor. Saved poses live in " + PoseLibrary.Directory + " and work on any character.");
                return;
            }
            int shown = a.InSavedPose ? Array.FindIndex(saved, p => string.Equals(p, a.SavedPoseName, StringComparison.OrdinalIgnoreCase)) : -1;
            int index = Mathf.Max(0, shown);
            int picked = _kit.Selector("Pose", saved, index);
            bool apply = picked != index;
            if (shown < 0 && _kit.Action("Apply Pose", saved[picked]))
            {
                apply = true;
            }
            if (apply)
            {
                _poseError = a.ApplySavedPose(saved[picked], PoseLibrary.Load(saved[picked]), out string error) ? null : error;
            }
            if (a.InSavedPose)
            {
                _kit.Value("Applied", a.SavedPoseName);
            }
            if (_poseError != null)
            {
                _kit.Note("Pose failed: " + _poseError);
            }
            _kit.Note("Your own poses from the pose editor. Applying one replaces the character's pose; fine-tune it in the pose editor (Ctrl+P) and save again to update it.");
        }

        private void DrawHandWeapon(PhotoActor a)
        {
            var labels = new List<string>();
            var slots = new List<EFT.InventoryLogic.EquipmentSlot?>();
            if (a.InHandsWeapon != null || a.HeldSlot.HasValue)
            {
                labels.Add("Main");
                slots.Add(null);
            }
            if (a.HasSidearm)
            {
                labels.Add("Pistol");
                slots.Add(EFT.InventoryLogic.EquipmentSlot.Holster);
            }
            if (a.HasKnife)
            {
                labels.Add("Knife");
                slots.Add(EFT.InventoryLogic.EquipmentSlot.Scabbard);
            }
            if (labels.Count < 2)
            {
                return;
            }
            int current = Mathf.Max(0, slots.IndexOf(a.HeldSlot));
            int picked = _kit.Selector("Hand Weapon", labels.ToArray(), current);
            if (picked != current)
            {
                a.SetHeldSlot(slots[picked]);
            }
        }

        private void DrawDualWield(PhotoActor a)
        {
            if (!a.CanDualWield && !a.DualWield)
            {
                return;
            }
            bool dual = _kit.Toggle("Dual Wield Pistols", a.DualWield);
            if (dual != a.DualWield)
            {
                a.SetDualWield(dual);
            }
        }

        partial void AddPoseCategories(List<string> categories);

        partial void DrawPoseCategory(string name, PhotoActor actor, ref bool handled);

        private void DrawActorPose(PhotoActor a)
        {
            HashSet<string> favorites = Favorites();
            _poseLibrary = _kit.Selector("Library", new[] { "Gear Rack Poses", "Body Animations" }, _poseLibrary);
            List<GearRackPose> list;
            if (_poseLibrary == 1)
            {
                _kit.Value("Animations", BodyClipLibrary.Status);
                var groups = new List<string> { "All", "Favorites" };
                groups.AddRange(BodyClipLibrary.Groups.Where(g => BodyClipLibrary.Poses.Any(p => p.Group == g && p.StateExists != false)));
                _bodyGroup = Mathf.Clamp(_bodyGroup, 0, groups.Count - 1);
                _bodyGroup = _kit.Selector("Group", groups.ToArray(), _bodyGroup);
                _bodyPartial = _kit.Toggle("Show Partial Body", _bodyPartial);
                _poseSearch = _kit.Text("Search", _poseSearch);
                string group = groups[_bodyGroup];
                list = BodyClipLibrary.Poses
                    .Where(p => p.StateExists != false)
                    .Where(p => _bodyPartial || p.Coverage == BodyCoverage.FullBody || p.Coverage == BodyCoverage.Untested)
                    .Where(p => group == "All" || (group == "Favorites" ? favorites.Contains(p.PoseName) : p.Group == group))
                    .Where(p => MatchesSearch(p))
                    .OrderBy(p => p.DisplayName)
                    .ToList();
            }
            else
            {
                var categories = new List<string> { "All", "Favorites", "Unlocked", "From Mods", "DuckCam" };
                AddPoseCategories(categories);
                _poseCategory = _kit.Selector("Category", categories.ToArray(), Mathf.Clamp(_poseCategory, 0, categories.Count - 1));
                if (_poseCategory == 4)
                {
                    DrawSavedPoses(a);
                    return;
                }
                if (_poseCategory >= 5)
                {
                    bool handled = false;
                    DrawPoseCategory(categories[_poseCategory], a, ref handled);
                    if (handled)
                    {
                        return;
                    }
                }
                _poseSearch = _kit.Text("Search", _poseSearch);
                list = GearRackPoseLibrary.Poses
                    .Where(p => p.StateExists != false)
                    .Where(p => _poseCategory != 1 || favorites.Contains(p.PoseName))
                    .Where(p => _poseCategory != 3 || p.ModClip != null)
                    .Where(p => (_poseCategory != 2 && !DuckCamConfig.OnlyUnlockedPoses.Value) || p.Unlocked)
                    .Where(p => MatchesSearch(p))
                    .OrderBy(p => p.DisplayName)
                    .ToList();
            }

            if (list.Count == 0)
            {
                _kit.Value("Pose", "No poses match");
            }
            else
            {
                if (a.Stance != null)
                {
                    if (a.InStance)
                    {
                        _kit.Value("Stance", "As in the raid" + (a.Stance.HasWeapon ? " · " + a.Stance.WeaponName : ""));
                        _kit.Note("This character copies the real one's pose and in-hands item. Picking a pose below replaces it; edit it in the pose editor (Ctrl+P).");
                    }
                    else if (_kit.Action("Raid Stance", "Restore"))
                    {
                        a.RestoreStance();
                    }
                }
                int shown = Mathf.Max(0, list.IndexOf(a.CurrentPose));
                string[] names = list.Select(PoseLabel).ToArray();
                int picked = _kit.Selector("Pose", names, shown);
                if (picked != shown)
                {
                    a.ClearStance();
                    a.ClearSavedPose();
                    float time = _poseLibrary == 1 && a.NormalizedTime <= 0.001f ? 0.5f : a.NormalizedTime;
                    _poseError = a.ApplyPose(list[picked], time, out string error) ? null : error;
                }
                if ((a.CurrentPose == null || a.InStance) && _kit.Action("Apply Pose", names[shown]))
                {
                    a.ClearStance();
                    a.ClearSavedPose();
                    _poseError = a.ApplyPose(list[shown], _poseLibrary == 1 ? 0.5f : 0f, out string error) ? null : error;
                }
                GearRackPose pose = a.CurrentPose;
                if (pose != null)
                {
                    bool fav = favorites.Contains(pose.PoseName);
                    bool newFav = _kit.Toggle("Favorite Pose", fav);
                    if (newFav != fav)
                    {
                        SetFavorite(favorites, pose.PoseName, newFav);
                    }
                }
            }

            if (a.Animate)
            {
                a.SetAnimate(false);
            }
            if (a.CurrentPose != null)
            {
                float t = _kit.Slider("Clip Time", a.NormalizedTime, 0f, 1f, 0.02f);
                if (Mathf.Abs(t - a.NormalizedTime) > 0.0001f)
                {
                    a.SetNormalizedTime(t);
                }
            }
            if (_poseError != null)
            {
                _kit.Note("Pose failed: " + _poseError);
            }
            if (_poseLibrary == 1)
            {
                _kit.Note("Body animations are the game's own in-raid animations (idle, crouch, prone, aiming, walking, reloading, leaning, throwing and more). "
                          + "Each one is tested on your character first; ones that don't fit are hidden. Partial clips only move the upper body or the legs. "
                          + "Use Clip Time to pick the frame. Weapon-holding animations look best with Weapon In Hands on.");
            }
        }

        private bool MatchesSearch(GearRackPose p)
        {
            return string.IsNullOrEmpty(_poseSearch)
                   || p.DisplayName.IndexOf(_poseSearch, StringComparison.OrdinalIgnoreCase) >= 0
                   || p.PoseName.IndexOf(_poseSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string PoseLabel(GearRackPose p)
        {
            if (p.IsBodyAnimation)
            {
                return p.DisplayName + (p.Coverage == BodyCoverage.UpperBody || p.Coverage == BodyCoverage.LowerBody
                    ? " (" + BodyClipLibrary.CoverageText(p.Coverage) + ")"
                    : "");
            }
            return p.DisplayName + (p.ModClip != null ? " [mod]" : "") + (p.Unlocked ? "" : " (locked)");
        }

        private static HashSet<string> Favorites()
        {
            return new HashSet<string>((DuckCamConfig.FavoritePoses.Value ?? "")
                .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim()));
        }

        private static void SetFavorite(HashSet<string> favorites, string poseName, bool on)
        {
            if (on)
            {
                favorites.Add(poseName);
            }
            else
            {
                favorites.Remove(poseName);
            }
            DuckCamConfig.FavoritePoses.Value = string.Join(",", favorites);
        }
    }
}
