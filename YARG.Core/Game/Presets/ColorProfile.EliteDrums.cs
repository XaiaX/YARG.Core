using System.Drawing;
using System.IO;
using YARG.Core.Extensions;
using YARG.Core.Utility;

namespace YARG.Core.Game
{
    public partial class ColorProfile
    {
        /// <summary>Fixed native visual positions. Lefty changes placement, not these color identities.</summary>
        public enum EliteDrumsFret { Kick, Snare, Hat, LeftCrashTom1, RideTom2, RightCrashTom3, DoubleKick }

        public partial class EliteDrumsColors : IFretColorProvider, IBinarySerializable
        {
            public Color SnareNote = DefaultRed;
            public Color HatIndifferentNote = DefaultYellowCymbal;
            public Color HatOpenNote = DefaultYellowCymbal;
            public Color HatClosedNote = DefaultYellowCymbal;
            public Color LeftCrashNote = DefaultBlueCymbal;
            public Color RideNote = DefaultOrange;
            public Color RightCrashNote = DefaultGreenCymbal;
            public Color Tom1Note = DefaultBlue;
            public Color Tom2Note = DefaultOrange;
            public Color Tom3Note = DefaultGreen;
            public Color KickNote = DefaultPurple;
            public Color DoubleKickNote = DefaultSilver;
            public Color KickFlamNote = DefaultSilver;
            public Color StompNote = DefaultOrange;
            public Color SplashNote = DefaultOrange;
            public Color HandFlamNote = DefaultRed;
            public Color WildcardNote = DefaultWildcard;

            public Color SnareStarpower = DefaultStarpower;
            public Color HatIndifferentStarpower = DefaultStarpower;
            public Color HatOpenStarpower = DefaultStarpower;
            public Color HatClosedStarpower = DefaultStarpower;
            public Color LeftCrashStarpower = DefaultStarpower;
            public Color RideStarpower = DefaultStarpower;
            public Color RightCrashStarpower = DefaultStarpower;
            public Color Tom1Starpower = DefaultStarpower;
            public Color Tom2Starpower = DefaultStarpower;
            public Color Tom3Starpower = DefaultStarpower;
            public Color KickStarpower = DefaultStarpower;
            public Color DoubleKickStarpower = DefaultStarpower;
            public Color KickFlamStarpower = DefaultStarpower;
            public Color StompStarpower = DefaultStarpower;
            public Color SplashStarpower = DefaultStarpower;
            public Color HandFlamStarpower = DefaultStarpower;
            public Color WildcardStarpower = DefaultWildcardStarpower;

            public Color KickFret = Color.FromArgb(255, 230, 63, 255);
            public Color SnareFret = DefaultRed;
            public Color HatFret = DefaultYellow;
            public Color LeftCrashTom1Fret = DefaultBlue;
            public Color RideTom2Fret = DefaultOrange;
            public Color RightCrashTom3Fret = DefaultGreen;
            public Color DoubleKickFret = DefaultSilverFret;
            public Color KickFretInner = DefaultPurple;
            public Color SnareFretInner = DefaultRed;
            public Color HatFretInner = DefaultYellow;
            public Color LeftCrashTom1FretInner = DefaultBlue;
            public Color RideTom2FretInner = DefaultOrange;
            public Color RightCrashTom3FretInner = DefaultGreen;
            public Color DoubleKickFretInner = DefaultSilverFret;
            public Color KickParticles = Color.FromArgb(255, 213, 0, 255);
            public Color SnareParticles = DefaultRed;
            public Color HatParticles = DefaultYellow;
            public Color LeftCrashTom1Particles = DefaultBlue;
            public Color RideTom2Particles = DefaultOrange;
            public Color RightCrashTom3Particles = DefaultGreen;
            public Color DoubleKickParticles = DefaultSilverFret;
            public Color Miss = DefaultMiss;
            public Color Metal = DefaultMetal;
            public Color MetalStarPower = DefaultMetalStarPower;

            public Color GetNoteColor(EliteDrumsColorRole role) => role switch
            {
                EliteDrumsColorRole.Snare => SnareNote,
                EliteDrumsColorRole.HatIndifferent => HatIndifferentNote,
                EliteDrumsColorRole.HatOpen => HatOpenNote,
                EliteDrumsColorRole.HatClosed => HatClosedNote,
                EliteDrumsColorRole.LeftCrash => LeftCrashNote,
                EliteDrumsColorRole.Ride => RideNote,
                EliteDrumsColorRole.RightCrash => RightCrashNote,
                EliteDrumsColorRole.Tom1 => Tom1Note,
                EliteDrumsColorRole.Tom2 => Tom2Note,
                EliteDrumsColorRole.Tom3 => Tom3Note,
                EliteDrumsColorRole.Kick => KickNote,
                EliteDrumsColorRole.DoubleKick => DoubleKickNote,
                EliteDrumsColorRole.KickFlam => KickFlamNote,
                EliteDrumsColorRole.Stomp => StompNote,
                EliteDrumsColorRole.Splash => SplashNote,
                EliteDrumsColorRole.HandFlam => HandFlamNote,
                EliteDrumsColorRole.Wildcard => DefaultWildcard,
                _ => default
            };

            public Color GetNoteStarPowerColor(EliteDrumsColorRole role) => role switch
            {
                EliteDrumsColorRole.Snare => SnareStarpower,
                EliteDrumsColorRole.HatIndifferent => HatIndifferentStarpower,
                EliteDrumsColorRole.HatOpen => HatOpenStarpower,
                EliteDrumsColorRole.HatClosed => HatClosedStarpower,
                EliteDrumsColorRole.LeftCrash => LeftCrashStarpower,
                EliteDrumsColorRole.Ride => RideStarpower,
                EliteDrumsColorRole.RightCrash => RightCrashStarpower,
                EliteDrumsColorRole.Tom1 => Tom1Starpower,
                EliteDrumsColorRole.Tom2 => Tom2Starpower,
                EliteDrumsColorRole.Tom3 => Tom3Starpower,
                EliteDrumsColorRole.Kick => KickStarpower,
                EliteDrumsColorRole.DoubleKick => DoubleKickStarpower,
                EliteDrumsColorRole.KickFlam => KickFlamStarpower,
                EliteDrumsColorRole.Stomp => StompStarpower,
                EliteDrumsColorRole.Splash => SplashStarpower,
                EliteDrumsColorRole.HandFlam => HandFlamStarpower,
                EliteDrumsColorRole.Wildcard => DefaultWildcardStarpower,
                _ => default
            };

            public Color GetFretColor(int index) => (EliteDrumsFret) index switch
            {
                EliteDrumsFret.Kick => KickFret, EliteDrumsFret.Snare => SnareFret,
                EliteDrumsFret.Hat => HatFret, EliteDrumsFret.LeftCrashTom1 => LeftCrashTom1Fret,
                EliteDrumsFret.RideTom2 => RideTom2Fret, EliteDrumsFret.RightCrashTom3 => RightCrashTom3Fret,
                EliteDrumsFret.DoubleKick => DoubleKickFret, _ => default
            };
            public Color GetFretInnerColor(int index) => (EliteDrumsFret) index switch
            {
                EliteDrumsFret.Kick => KickFretInner, EliteDrumsFret.Snare => SnareFretInner,
                EliteDrumsFret.Hat => HatFretInner, EliteDrumsFret.LeftCrashTom1 => LeftCrashTom1FretInner,
                EliteDrumsFret.RideTom2 => RideTom2FretInner, EliteDrumsFret.RightCrashTom3 => RightCrashTom3FretInner,
                EliteDrumsFret.DoubleKick => DoubleKickFretInner, _ => default
            };
            public Color GetParticleColor(int index) => (EliteDrumsFret) index switch
            {
                EliteDrumsFret.Kick => KickParticles, EliteDrumsFret.Snare => SnareParticles,
                EliteDrumsFret.Hat => HatParticles, EliteDrumsFret.LeftCrashTom1 => LeftCrashTom1Particles,
                EliteDrumsFret.RideTom2 => RideTom2Particles, EliteDrumsFret.RightCrashTom3 => RightCrashTom3Particles,
                EliteDrumsFret.DoubleKick => DoubleKickParticles, _ => default
            };
            public Color GetMetalColor(bool isForStarPower) => isForStarPower ? MetalStarPower : Metal;
            public EliteDrumsColors Copy() => (EliteDrumsColors) MemberwiseClone();

            public void Serialize(BinaryWriter writer)
            {
                writer.Write(SnareNote); writer.Write(HatIndifferentNote); writer.Write(HatOpenNote); writer.Write(HatClosedNote);
                writer.Write(LeftCrashNote); writer.Write(RideNote); writer.Write(RightCrashNote);
                writer.Write(Tom1Note); writer.Write(Tom2Note); writer.Write(Tom3Note);
                writer.Write(KickNote); writer.Write(DoubleKickNote); writer.Write(KickFlamNote);
                writer.Write(StompNote); writer.Write(SplashNote); writer.Write(HandFlamNote); writer.Write(WildcardNote);
                writer.Write(SnareStarpower); writer.Write(HatIndifferentStarpower); writer.Write(HatOpenStarpower); writer.Write(HatClosedStarpower);
                writer.Write(LeftCrashStarpower); writer.Write(RideStarpower); writer.Write(RightCrashStarpower);
                writer.Write(Tom1Starpower); writer.Write(Tom2Starpower); writer.Write(Tom3Starpower);
                writer.Write(KickStarpower); writer.Write(DoubleKickStarpower); writer.Write(KickFlamStarpower);
                writer.Write(StompStarpower); writer.Write(SplashStarpower); writer.Write(HandFlamStarpower); writer.Write(WildcardStarpower);
                writer.Write(KickFret); writer.Write(SnareFret); writer.Write(HatFret); writer.Write(LeftCrashTom1Fret);
                writer.Write(RideTom2Fret); writer.Write(RightCrashTom3Fret); writer.Write(DoubleKickFret);
                writer.Write(KickFretInner); writer.Write(SnareFretInner); writer.Write(HatFretInner); writer.Write(LeftCrashTom1FretInner);
                writer.Write(RideTom2FretInner); writer.Write(RightCrashTom3FretInner); writer.Write(DoubleKickFretInner);
                writer.Write(KickParticles); writer.Write(SnareParticles); writer.Write(HatParticles); writer.Write(LeftCrashTom1Particles);
                writer.Write(RideTom2Particles); writer.Write(RightCrashTom3Particles); writer.Write(DoubleKickParticles);
                writer.Write(Miss); writer.Write(Metal); writer.Write(MetalStarPower);
                SerializeInputColors(writer);
            }

            public void Deserialize(BinaryReader reader, int version = 0)
            {
                SnareNote = reader.ReadColor(); HatIndifferentNote = reader.ReadColor(); HatOpenNote = reader.ReadColor(); HatClosedNote = reader.ReadColor();
                LeftCrashNote = reader.ReadColor(); RideNote = reader.ReadColor(); RightCrashNote = reader.ReadColor();
                Tom1Note = reader.ReadColor(); Tom2Note = reader.ReadColor(); Tom3Note = reader.ReadColor();
                KickNote = reader.ReadColor(); DoubleKickNote = reader.ReadColor(); KickFlamNote = reader.ReadColor();
                StompNote = reader.ReadColor(); SplashNote = reader.ReadColor(); HandFlamNote = reader.ReadColor(); WildcardNote = reader.ReadColor();
                SnareStarpower = reader.ReadColor(); HatIndifferentStarpower = reader.ReadColor(); HatOpenStarpower = reader.ReadColor(); HatClosedStarpower = reader.ReadColor();
                LeftCrashStarpower = reader.ReadColor(); RideStarpower = reader.ReadColor(); RightCrashStarpower = reader.ReadColor();
                Tom1Starpower = reader.ReadColor(); Tom2Starpower = reader.ReadColor(); Tom3Starpower = reader.ReadColor();
                KickStarpower = reader.ReadColor(); DoubleKickStarpower = reader.ReadColor(); KickFlamStarpower = reader.ReadColor();
                StompStarpower = reader.ReadColor(); SplashStarpower = reader.ReadColor(); HandFlamStarpower = reader.ReadColor(); WildcardStarpower = reader.ReadColor();
                KickFret = reader.ReadColor(); SnareFret = reader.ReadColor(); HatFret = reader.ReadColor(); LeftCrashTom1Fret = reader.ReadColor();
                RideTom2Fret = reader.ReadColor(); RightCrashTom3Fret = reader.ReadColor(); DoubleKickFret = reader.ReadColor();
                KickFretInner = reader.ReadColor(); SnareFretInner = reader.ReadColor(); HatFretInner = reader.ReadColor(); LeftCrashTom1FretInner = reader.ReadColor();
                RideTom2FretInner = reader.ReadColor(); RightCrashTom3FretInner = reader.ReadColor(); DoubleKickFretInner = reader.ReadColor();
                KickParticles = reader.ReadColor(); SnareParticles = reader.ReadColor(); HatParticles = reader.ReadColor(); LeftCrashTom1Particles = reader.ReadColor();
                RideTom2Particles = reader.ReadColor(); RightCrashTom3Particles = reader.ReadColor(); DoubleKickParticles = reader.ReadColor();
                Miss = reader.ReadColor(); Metal = reader.ReadColor(); MetalStarPower = reader.ReadColor();
                if (version >= 4) DeserializeInputColors(reader);
                else MigrateInputColors();
            }
        }
    }
}
