using System.Drawing;
using System.IO;
using YARG.Core.Extensions;

namespace YARG.Core.Game
{
    public partial class ColorProfile
    {
        public partial class EliteDrumsColors
        {
            // Independent physical-input colors. Merged-position fret colors remain in the original payload.
            public Color SnareInputFret = DefaultRed;
            public Color HatIndifferentInputFret = DefaultYellow;
            public Color HatOpenInputFret = DefaultYellow;
            public Color HatClosedInputFret = DefaultYellow;
            public Color LeftCrashInputFret = DefaultBlue;
            public Color RideInputFret = DefaultOrange;
            public Color RightCrashInputFret = DefaultGreen;
            public Color Tom1InputFret = DefaultBlue;
            public Color Tom2InputFret = DefaultOrange;
            public Color Tom3InputFret = DefaultGreen;
            public Color KickInputFret = DefaultPurple;
            public Color DoubleKickInputFret = DefaultSilverFret;
            public Color KickFlamInputFret = DefaultPurple;
            public Color StompInputFret = DefaultYellow;
            public Color SplashInputFret = DefaultYellow;
            public Color HandFlamInputFret = DefaultRed;

            public Color SnareInputEffect = DefaultRed;
            public Color HatIndifferentInputEffect = DefaultYellow;
            public Color HatOpenInputEffect = DefaultYellow;
            public Color HatClosedInputEffect = DefaultYellow;
            public Color LeftCrashInputEffect = DefaultBlue;
            public Color RideInputEffect = DefaultOrange;
            public Color RightCrashInputEffect = DefaultGreen;
            public Color Tom1InputEffect = DefaultBlue;
            public Color Tom2InputEffect = DefaultOrange;
            public Color Tom3InputEffect = DefaultGreen;
            public Color KickInputEffect = Color.FromArgb(255, 213, 0, 255);
            public Color DoubleKickInputEffect = DefaultSilverFret;
            public Color KickFlamInputEffect = Color.FromArgb(255, 213, 0, 255);
            public Color StompInputEffect = DefaultYellow;
            public Color SplashInputEffect = DefaultYellow;
            public Color HandFlamInputEffect = DefaultRed;

            public Color GetInputFretColor(EliteDrumsColorRole role) => role switch
            {
                EliteDrumsColorRole.Snare => SnareInputFret,
                EliteDrumsColorRole.HatIndifferent => HatIndifferentInputFret,
                EliteDrumsColorRole.HatOpen => HatOpenInputFret,
                EliteDrumsColorRole.HatClosed => HatClosedInputFret,
                EliteDrumsColorRole.LeftCrash => LeftCrashInputFret,
                EliteDrumsColorRole.Ride => RideInputFret,
                EliteDrumsColorRole.RightCrash => RightCrashInputFret,
                EliteDrumsColorRole.Tom1 => Tom1InputFret,
                EliteDrumsColorRole.Tom2 => Tom2InputFret,
                EliteDrumsColorRole.Tom3 => Tom3InputFret,
                EliteDrumsColorRole.Kick => KickInputFret,
                EliteDrumsColorRole.DoubleKick => DoubleKickInputFret,
                EliteDrumsColorRole.KickFlam => KickFlamInputFret,
                EliteDrumsColorRole.Stomp => StompInputFret,
                EliteDrumsColorRole.Splash => SplashInputFret,
                EliteDrumsColorRole.HandFlam => HandFlamInputFret,
                _ => DefaultWildcard
            };

            public Color GetInputEffectColor(EliteDrumsColorRole role) => role switch
            {
                EliteDrumsColorRole.Snare => SnareInputEffect,
                EliteDrumsColorRole.HatIndifferent => HatIndifferentInputEffect,
                EliteDrumsColorRole.HatOpen => HatOpenInputEffect,
                EliteDrumsColorRole.HatClosed => HatClosedInputEffect,
                EliteDrumsColorRole.LeftCrash => LeftCrashInputEffect,
                EliteDrumsColorRole.Ride => RideInputEffect,
                EliteDrumsColorRole.RightCrash => RightCrashInputEffect,
                EliteDrumsColorRole.Tom1 => Tom1InputEffect,
                EliteDrumsColorRole.Tom2 => Tom2InputEffect,
                EliteDrumsColorRole.Tom3 => Tom3InputEffect,
                EliteDrumsColorRole.Kick => KickInputEffect,
                EliteDrumsColorRole.DoubleKick => DoubleKickInputEffect,
                EliteDrumsColorRole.KickFlam => KickFlamInputEffect,
                EliteDrumsColorRole.Stomp => StompInputEffect,
                EliteDrumsColorRole.Splash => SplashInputEffect,
                EliteDrumsColorRole.HandFlam => HandFlamInputEffect,
                _ => DefaultWildcard
            };

            private void SerializeInputColors(BinaryWriter writer)
            {
                writer.Write(SnareInputFret); writer.Write(HatIndifferentInputFret); writer.Write(HatOpenInputFret);
                writer.Write(HatClosedInputFret); writer.Write(LeftCrashInputFret); writer.Write(RideInputFret);
                writer.Write(RightCrashInputFret); writer.Write(Tom1InputFret); writer.Write(Tom2InputFret);
                writer.Write(Tom3InputFret); writer.Write(KickInputFret); writer.Write(DoubleKickInputFret);
                writer.Write(KickFlamInputFret); writer.Write(StompInputFret); writer.Write(SplashInputFret);
                writer.Write(HandFlamInputFret);
                writer.Write(SnareInputEffect); writer.Write(HatIndifferentInputEffect); writer.Write(HatOpenInputEffect);
                writer.Write(HatClosedInputEffect); writer.Write(LeftCrashInputEffect); writer.Write(RideInputEffect);
                writer.Write(RightCrashInputEffect); writer.Write(Tom1InputEffect); writer.Write(Tom2InputEffect);
                writer.Write(Tom3InputEffect); writer.Write(KickInputEffect); writer.Write(DoubleKickInputEffect);
                writer.Write(KickFlamInputEffect); writer.Write(StompInputEffect); writer.Write(SplashInputEffect);
                writer.Write(HandFlamInputEffect);
            }

            private void DeserializeInputColors(BinaryReader reader)
            {
                SnareInputFret = reader.ReadColor(); HatIndifferentInputFret = reader.ReadColor(); HatOpenInputFret = reader.ReadColor();
                HatClosedInputFret = reader.ReadColor(); LeftCrashInputFret = reader.ReadColor(); RideInputFret = reader.ReadColor();
                RightCrashInputFret = reader.ReadColor(); Tom1InputFret = reader.ReadColor(); Tom2InputFret = reader.ReadColor();
                Tom3InputFret = reader.ReadColor(); KickInputFret = reader.ReadColor(); DoubleKickInputFret = reader.ReadColor();
                KickFlamInputFret = reader.ReadColor(); StompInputFret = reader.ReadColor(); SplashInputFret = reader.ReadColor();
                HandFlamInputFret = reader.ReadColor();
                SnareInputEffect = reader.ReadColor(); HatIndifferentInputEffect = reader.ReadColor(); HatOpenInputEffect = reader.ReadColor();
                HatClosedInputEffect = reader.ReadColor(); LeftCrashInputEffect = reader.ReadColor(); RideInputEffect = reader.ReadColor();
                RightCrashInputEffect = reader.ReadColor(); Tom1InputEffect = reader.ReadColor(); Tom2InputEffect = reader.ReadColor();
                Tom3InputEffect = reader.ReadColor(); KickInputEffect = reader.ReadColor(); DoubleKickInputEffect = reader.ReadColor();
                KickFlamInputEffect = reader.ReadColor(); StompInputEffect = reader.ReadColor(); SplashInputEffect = reader.ReadColor();
                HandFlamInputEffect = reader.ReadColor();
            }

            private void MigrateInputColors()
            {
                // Older presets only had merged-position effects. Preserve their chosen colors on upgrade.
                SnareInputFret = SnareFret; HatIndifferentInputFret = HatOpenInputFret = HatClosedInputFret = HatFret;
                LeftCrashInputFret = Tom1InputFret = LeftCrashTom1Fret;
                RideInputFret = Tom2InputFret = RideTom2Fret;
                RightCrashInputFret = Tom3InputFret = RightCrashTom3Fret;
                KickInputFret = KickFlamInputFret = KickFret; DoubleKickInputFret = DoubleKickFret;
                StompInputFret = SplashInputFret = HatFret; HandFlamInputFret = SnareFret;
                SnareInputEffect = SnareParticles;
                HatIndifferentInputEffect = HatOpenInputEffect = HatClosedInputEffect = HatParticles;
                LeftCrashInputEffect = Tom1InputEffect = LeftCrashTom1Particles;
                RideInputEffect = Tom2InputEffect = RideTom2Particles;
                RightCrashInputEffect = Tom3InputEffect = RightCrashTom3Particles;
                KickInputEffect = KickFlamInputEffect = KickParticles;
                DoubleKickInputEffect = DoubleKickParticles;
                StompInputEffect = SplashInputEffect = HatParticles; HandFlamInputEffect = SnareParticles;
            }
        }
    }
}
