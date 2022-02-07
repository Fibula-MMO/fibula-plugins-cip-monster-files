// -----------------------------------------------------------------
// <copyright file="MonFilesMonsterTypeLoader.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.Plugins.MonsterLoaders.CipMonFiles
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Fibula.Data.Contracts.Abstractions;
    using Fibula.Definitions.Data.Entities;
    using Fibula.Definitions.Data.Structures;
    using Fibula.Definitions.Enumerations;
    using Fibula.Definitions.Flags;
    using Fibula.Parsing.CipFiles;
    using Fibula.Parsing.CipFiles.Enumerations;
    using Fibula.Parsing.CipFiles.Extensions;
    using Fibula.Utilities.Validation;
    using Microsoft.Extensions.Logging;
    using Microsoft.Extensions.Options;

    /// <summary>
    /// Class that represents a monster type loader that reads from the .mon files.
    /// </summary>
    public sealed class MonFilesMonsterTypeLoader : IMonsterTypesLoader
    {
        /// <summary>
        /// Character for comments.
        /// </summary>
        public const char CommentSymbol = '#';

        /// <summary>
        /// Separator used for property and value pairs.
        /// </summary>
        public const char PropertyValueSeparator = '=';

        /// <summary>
        /// The extension for monster files.
        /// </summary>
        private const string MonsterFileExtension = "mon";

        /// <summary>
        /// The directory information for the monster files directory.
        /// </summary>
        private readonly DirectoryInfo monsterFilesDirInfo;

        /// <summary>
        /// Initializes a new instance of the <see cref="MonFilesMonsterTypeLoader"/> class.
        /// </summary>
        /// <param name="logger">A reference to the logger instance.</param>
        /// <param name="options">The options for this loader.</param>
        public MonFilesMonsterTypeLoader(
            ILogger<MonFilesMonsterTypeLoader> logger,
            IOptions<MonFilesMonsterTypeLoaderOptions> options)
        {
            logger.ThrowIfNull(nameof(logger));
            options.ThrowIfNull(nameof(options));

            DataAnnotationsValidator.ValidateObjectRecursive(options.Value);

            this.LoaderOptions = options.Value;
            this.Logger = logger;

            options.Value.MonsterFilesDirectory.ThrowIfNullOrWhiteSpace(nameof(options.Value.MonsterFilesDirectory));

            this.monsterFilesDirInfo = new DirectoryInfo(options.Value.MonsterFilesDirectory);
        }

        /// <summary>
        /// Gets the loader options.
        /// </summary>
        public MonFilesMonsterTypeLoaderOptions LoaderOptions { get; }

        /// <summary>
        /// Gets the logger to use in this handler.
        /// </summary>
        public ILogger Logger { get; }

        /// <summary>
        /// Attempts to load the monster catalog.
        /// </summary>
        /// <returns>The catalog, containing a mapping of loaded id to the monster types.</returns>
        public IDictionary<string, MonsterTypeEntity> LoadTypes()
        {
            var monsterTypesDictionary = new Dictionary<string, MonsterTypeEntity>();

            if (!this.monsterFilesDirInfo.Exists)
            {
                throw new InvalidDataException($"The specified {nameof(this.LoaderOptions.MonsterFilesDirectory)} could not be found.");
            }

            foreach (var monsterFileInfo in this.monsterFilesDirInfo.GetFiles($"*.{MonsterFileExtension}"))
            {
                var monsterType = ReadMonsterFile(monsterFileInfo);

                if (monsterType != null)
                {
                    monsterTypesDictionary.Add(monsterType.RaceId, monsterType);
                }
            }

            return monsterTypesDictionary;
        }

        /// <summary>
        /// Reads a <see cref="MonsterTypeEntity"/> out of a monster file.
        /// </summary>
        /// <param name="monsterFileInfo">The information about the monster file.</param>
        /// <returns>The <see cref="MonsterTypeEntity"/> instance.</returns>
        private static MonsterTypeEntity ReadMonsterFile(FileInfo monsterFileInfo)
        {
            monsterFileInfo.ThrowIfNull(nameof(monsterFileInfo));

            if (!monsterFileInfo.Exists)
            {
                return null;
            }

            var parsedMonster = CipFileParser.ParseMonsterFile(monsterFileInfo);

            var monsterType = new MonsterTypeEntity()
            {
                RaceId = parsedMonster.RaceId.ToString(),
                Name = parsedMonster.Name,
                Article = parsedMonster.Article,
                OriginalOutfit = new Outfit()
                {
                    Id = parsedMonster.Outfit.Id,
                    Head = parsedMonster.Outfit.Head,
                    Body = parsedMonster.Outfit.Body,
                    Legs = parsedMonster.Outfit.Legs,
                    Feet = parsedMonster.Outfit.Feet,
                },
                Corpse = (ushort)parsedMonster.Corpse,
                BloodType = parsedMonster.BloodType,
                BaseExperienceYield = parsedMonster.Experience,
                SummonCost = parsedMonster.SummonCost,
                HitpointFleeThreshold = parsedMonster.FleeThreshold,
                BaseAttack = parsedMonster.Attack,
                BaseDefense = parsedMonster.Defense,
                BaseArmorRating = parsedMonster.Armor,
                LoseTargetDistance = parsedMonster.LoseTarget,
                Strategy = parsedMonster.Strategy,
            };

            foreach (var flag in parsedMonster.Flags)
            {
                if (flag.ToCreatureFlag() is CreatureFlag creatureFlag)
                {
                    monsterType.SetCreatureFlag(creatureFlag);
                }
            }

            foreach (var (skillType, defaultLevel, currentLevel, maximumLevel, targetCount, countIncreaseFactor, increaserPerLevel) in parsedMonster.Skills)
            {
                if (!Enum.TryParse(skillType, ignoreCase: true, out CipMonsterSkillType mSkill))
                {
                    continue;
                }

                switch (mSkill)
                {
                    case CipMonsterSkillType.Hitpoints:
                        monsterType.MaxHitpoints = currentLevel < 0 ? ushort.MaxValue : (ushort)defaultLevel;
                        break;
                    case CipMonsterSkillType.GoStrength:
                        monsterType.BaseSpeed = currentLevel < 0 ? ushort.MinValue : (ushort)defaultLevel;
                        break;
                    case CipMonsterSkillType.CarryStrength:
                        monsterType.Capacity = currentLevel < 0 ? ushort.MinValue : (ushort)defaultLevel;
                        break;
                    case CipMonsterSkillType.FistFighting:
                        if (currentLevel > 0)
                        {
                            monsterType.SetSkill(SkillType.NoWeapon, Convert.ToInt32(currentLevel), Convert.ToInt32(defaultLevel), Convert.ToInt32(maximumLevel), targetCount, countIncreaseFactor, increaserPerLevel);
                        }

                        break;
                }
            }

            foreach (var spellRule in parsedMonster.Spells)
            {
                // Not implemented yet.
            }

            monsterType.SetInventory(parsedMonster.Inventory);
            monsterType.SetPhrases(parsedMonster.Phrases);

            return monsterType;
        }
    }
}
