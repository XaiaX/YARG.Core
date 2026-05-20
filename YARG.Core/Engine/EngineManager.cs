using System;
using System.Collections.Generic;
using YARG.Core.Chart;
using YARG.Core.Game;
using YARG.Core.Logging;

namespace YARG.Core.Engine
{
    // Tracks and instantiates engines, handles IPC between engines, and events that affect multiple engines
    public partial class EngineManager
    {
        public const int FREE_HARMONY_INDEX = -1;

        private int                      _nextEngineIndex;
        List <EngineContainer>           _allEngines     = new();
        Dictionary<int, EngineContainer> _allEnginesById = new();

        public List<EngineContainer> Engines => _allEngines;

        private SongChart?               _chart;

        public partial class EngineContainer
        {
            public  int             EngineId         { get; }
            public  BaseEngine      Engine           { get; }
            public  Instrument      Instrument       { get; }
            public  int             HarmonyIndex     { get; }
            private SongChart       SongChart        { get; }
            public  List<Phrase>    UnisonPhrases    { get; }
            public  RockMeterPreset RockMeterPreset  { get; }

            private List<EngineCommand> _sentCommands = new();
            private int                 _commandCount => _sentCommands.Count;
            private EngineManager       _engineManager;

            public EngineContainer(BaseEngine engine, Instrument instrument, int harmonyIndex, SongChart songChart, int engineId, EngineManager manager, RockMeterPreset rockMeterPreset)
            {
                EngineId = engineId;
                Engine = engine;
                Instrument = instrument;
                HarmonyIndex = harmonyIndex;
                SongChart = songChart;
                UnisonPhrases = GetUnisonPhrases(Instrument, SongChart);
                RockMeterPreset = rockMeterPreset;
                _engineManager = manager;
                Happiness = rockMeterPreset.StartingHappiness;

                SubscribeToEngineEvents();
            }

            public void SendCommand(EngineCommandType command)
            {
                // TODO: This will require rethinking when there are more commands, but for now this should work?
                if (command == EngineCommandType.AwardUnisonBonus)
                {
                    Engine.AwardUnisonBonus();
                }
                else
                {
                    return;
                }
                _sentCommands.Add(new EngineCommand { CommandType = command, Time = Engine.CurrentTime });
            }

            public void OnStarPowerPhraseHit<TNote>(TNote note) where TNote : Note<TNote>
            {
                _engineManager.OnStarPowerPhraseHit(this, note.Time);
            }

            public void UpdateEngine(double time)
            {
                Engine.Update(time);
            }

            private void OnStarPowerStatus(bool active)
            {
                var count = _engineManager._starpowerCount;
                count += active ? 1 : -1;
                _engineManager.UpdateStarPowerCount(count);
            }
        }

        /// <summary>
        /// Registers an engine with harmony index 0. Requires harmonyIndex >= 0.
        /// </summary>
        public EngineContainer Register<TEngineType>(TEngineType engine, Instrument instrument, SongChart chart, RockMeterPreset rockMeterPreset)
            where TEngineType : BaseEngine
        {
            return Register(engine, instrument, 0, chart, rockMeterPreset);
        }

        
        /// <summary>
        /// Registers an engine for free vocals, using HarmonyIndex = FREE_HARMONY_INDEX.
        /// </summary>
        public EngineContainer Register<TEngineType>(TEngineType engine, Instrument instrument, bool freeVocals, SongChart chart, RockMeterPreset rockMeterPreset)
            where TEngineType : BaseEngine
        {
            if (!freeVocals)
            {
                throw new ArgumentException("Use the indexed overload for non-free vocals registration");
            }

            if (_chart == null)
            {
                _chart = chart;
            }
            else
            {
                if (_chart != chart)
                {
                    throw new ArgumentException("Cannot register engine with different chart");
                }
            }

            var engineContainer = new EngineContainer(engine, instrument, FREE_HARMONY_INDEX, chart, _nextEngineIndex++, this, rockMeterPreset);

            _allEngines.Add(engineContainer);
            _allEnginesById.Add(engineContainer.EngineId, engineContainer);
            AddPlayerToUnisons(engineContainer);
            engine.OnCodaStart += CodaStartHandler;
            engine.OnCodaEnd += CodaEndHandler;

            return engineContainer;
        }

        /// <summary>
        /// Registers an engine with an explicit harmony index. Requires harmonyIndex >= 0.
        /// For free vocals, use the overload that accepts a boolean freeVocals parameter instead.
        /// </summary>
        public EngineContainer Register<TEngineType>(TEngineType engine, Instrument instrument, int harmonyIndex, SongChart chart, RockMeterPreset rockMeterPreset)
            where TEngineType : BaseEngine
        {
            if (harmonyIndex < 0)
            {
                YargLogger.FailFormat("Indexed Register requires harmonyIndex >= 0; got {0}", harmonyIndex);
            }

            if (_chart == null)
            {
                _chart = chart;
            }
            else
            {
                if (_chart != chart)
                {
                    throw new ArgumentException("Cannot register engine with different chart");
                }
            }

            var engineContainer = new EngineContainer(engine, instrument, harmonyIndex, chart, _nextEngineIndex++, this, rockMeterPreset);

            // _previousHappiness = rockMeterPreset.StartingHappiness;

            _allEngines.Add(engineContainer);
            _allEnginesById.Add(engineContainer.EngineId, engineContainer);
            AddPlayerToUnisons(engineContainer);
            engine.OnCodaStart += CodaStartHandler;
            engine.OnCodaEnd += CodaEndHandler;

            return engineContainer;
        }

        private EngineContainer GetEngineContainer(BaseEngine target)
        {
            foreach (var engine in _allEngines)
            {
                if (engine.Engine == target)
                {
                    return engine;
                }
            }
            throw new ArgumentException("Target engine not found");
        }

        private void UpdateStarPowerCount(int count)
        {
            _starpowerCount = Math.Clamp(count, 0, int.MaxValue);
            UpdateBandMultiplier();
        }

        public void Reset()
        {
            _activeCodaCount = 0;
            _currentStarIndex = 0;
            _previousHappiness = 100f;
            _starpowerCount = 0;
            // These values are derived from others, so there's no reason to reset them
            // Score = 0; derived from all players' Score + BandBonusScore
            // Stars = 0; derived from Score

            // Combo is calculated a bit differently, so we still reset it even though it's dependent on player combo
            Combo = 0;
            foreach (var engineContainer in _allEngines)
            {
                engineContainer.ResetHappiness();
            }

            foreach (var unisonEvent in _unisonEvents)
            {
                unisonEvent.Reset();
            }
        }

        public void UpdateEngines(double time)
        {
            foreach (var engine in _allEngines)
            {
                engine.UpdateEngine(time);
            }
        }

        public enum EngineCommandType
        {
            AwardUnisonBonus,
        }

        private struct EngineCommand
        {
            public EngineCommandType CommandType;
            public double            Time;
        }

        public void Unregister(EngineContainer engineContainer)
        {
            RemovePlayerFromUnisons(engineContainer);
            _allEngines.Remove(engineContainer);
            _allEnginesById.Remove(engineContainer.EngineId);
        }
    }
}