using System.Reflection;
using System.Globalization;
using Ponder;

namespace WarpTen.Ponder
{
    public static class Adapter
    {
        public static void Initialize(Assembly owner, string[] parts, float chargeSeconds, float minimumFlow, double hydrogenRate)
        {
            var timeline = new GuideTimeline(chargeSeconds, minimumFlow);
            var guide = new GuideDefinition
            {
                Id = "building_a_warp_drive",
                Title = "Building a Warp Drive",
                Parts = parts,
                Steps = new[]
                {
                    new GuideStep { Duration = 8, Caption = "Start with one Warp Core and two Warp Coil Infusers. Remove any other warp drives from this ship." },
                    new GuideStep { Duration = 10, Caption = "Place two infusers side by side, facing the same way. Keep them level and in line, with at least three metres between their centres." },
                    new GuideStep { Duration = 10, Caption = "Attach matching rows of coils behind the infusers. Aim at an infuser or coil to snap the next coil into place. Add matched pairs as ship mass increases." },
                    new GuideStep { Duration = 12, Caption = "Open the Warp Core and install an Annihilation Matrix in its Matrix tab. This four-coil starter uses a reusable Grade D matrix." },
                    new GuideStep { Duration = 13, Caption = "Attach plasma conduits to the core outlet and split the route with a T. Connect both infusers' front inlets. Aim at an open collar; scroll to rotate corners and T branches around the joint." },
                    new GuideStep { Duration = 10, Caption = "Close this quarter-metre gap with a Snap Spacer. Stack spacers for 0.25, 0.5 or 0.75 m offsets. Plasma needs touching, opposing collars: fluid pipes, splitters and tanks cannot carry it." },
                    new GuideStep { Duration = 12, Caption = "Supply hydrogen to a buffer tank, then a fluid valve, then core socket 2. Keep the buffer upstream of the valve so closing the valve stops fuel. The core needs no electricity." },
                    new GuideStep { Duration = 15, Caption = "Connect a throttle lever to a Signal Combinator set to Multiply. Feed its other input from a Constant, then connect its output to valve signal port 0. For these four coils and Grade D, set the Constant to " + hydrogenRate.ToString("0.###", CultureInfo.InvariantCulture) + " hydrogen/s." },
                    new GuideStep { Duration = 14, Caption = "Keep solid hull and machinery out of the space between the coil rows. The cyan planes mark the maximum ship width: three metres beyond each outer nacelle face. Press F9 to check these limits while building." },
                    new GuideStep { Duration = chargeSeconds, Caption = "Open the hydrogen valve. Both infusers need at least " + (minimumFlow * 100).ToString("0.##", CultureInfo.InvariantCulture) + "% rated flow throughout the " + chargeSeconds.ToString("0.##", CultureInfo.InvariantCulture) + "-second charge. Falling below that threshold resets charging." },
                    new GuideStep { Duration = 10, Caption = "When charging and entry checks pass, the drive enters warp automatically. The infusers face the direction of travel. Adjust the throttle to control speed." }
                },
                BuildScene = scene => scene.CreateObject("presentation").AddComponent<DriveGuidePresentation>().Build(scene, timeline, hydrogenRate),
                Sample = (scene, seconds) => scene.GetObject("presentation").GetComponent<DriveGuidePresentation>().Sample(scene, seconds)
            };
            PonderRegistry.Create("warpten", owner).RegisterGuide(guide);
        }
    }
}
