using Composition.Input;
using Composition.Layers;
using Composition.Nodes;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tools;

namespace UI.Nodes
{
    public class ProfileButtonNode : TextButtonNode
    {

        public event Action<Profile> ProfileSet;

        private Profile profile;

        public ProfileButtonNode(Profile profile, Color background, Color hover, Color textColor) 
            : base("Profile: " + profile.Name, background, hover, textColor)
        {
            this.profile = profile;
            TextNode.Alignment = RectangleAlignment.CenterRight;
            OnClick += ProfileButtonNode_OnClick;
        }

        private void ProfileButtonNode_OnClick(Composition.Input.MouseInputEvent mie)
        {
            MouseMenu mouseMenu = new MouseMenu(this);
            mouseMenu.LeftToRight = false;

            foreach (Profile profile in Profile.GetProfiles(PlatformTools.WorkingDirectory))
            {
                Profile temp = profile;
                mouseMenu.AddItem(profile.Name, () => { ProfileSet?.Invoke(temp); });
            }
            mouseMenu.Show(this);
            mouseMenu.AddBlank();

            mouseMenu.AddItem("Add New Profile", () =>
            {
                TextPopupNode textPopupNode = new TextPopupNode("Add Profile", "Profile Name", "");
                textPopupNode.OnOK += (string name) =>
                {
                    Profile profile = Profile.AddProfile(PlatformTools.WorkingDirectory, name);

                    if (profile != null)
                    {
                        ProfileSet?.Invoke(profile);
                    }
                };
                GetLayer<PopupLayer>().Popup(textPopupNode);
            });
            mouseMenu.AddItem("Edit Name '" + profile.Name + "'", () =>
            {
                TextPopupNode textPopupNode = new TextPopupNode("Edit Profile Name", "New Name", profile.Name);
                textPopupNode.OnOK += (string name) =>
                {
                    if (!Profile.RenameProfile(PlatformTools.WorkingDirectory, profile, name))
                    {
                        GetLayer<PopupLayer>().PopupMessage("Failed to rename");
                        return;
                    }

                    ProfileSet?.Invoke(profile);
                };
                GetLayer<PopupLayer>().Popup(textPopupNode);
            });
        }

    }
}
