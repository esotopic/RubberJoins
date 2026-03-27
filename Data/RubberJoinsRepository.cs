using Microsoft.Data.SqlClient;
using RubberJoins.Models;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace RubberJoins.Data
{
    public class RubberJoinsRepository
    {
        private readonly string _connectionString;

        public RubberJoinsRepository(string connectionString)
        {
            _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        }

        /// <summary>
        /// Creates all required tables if they don't exist and seeds initial data.
        /// </summary>
        public async Task EnsureTablesExistAsync()
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                // Create Users table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Users')
                        BEGIN
                            CREATE TABLE Users (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                Username NVARCHAR(100) NOT NULL UNIQUE,
                                PasswordHash NVARCHAR(500) NOT NULL,
                                Salt NVARCHAR(500) NOT NULL,
                                CreatedDate NVARCHAR(10) NOT NULL
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create Exercises table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Exercises')
                        BEGIN
                            CREATE TABLE Exercises (
                                Id NVARCHAR(50) PRIMARY KEY,
                                Name NVARCHAR(255) NOT NULL,
                                Category NVARCHAR(50) NOT NULL,
                                Targets NVARCHAR(500) NOT NULL,
                                Description NVARCHAR(MAX),
                                Cues NVARCHAR(MAX),
                                Explanation NVARCHAR(MAX),
                                Warning NVARCHAR(MAX),
                                Phases NVARCHAR(50)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create Supplements table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Supplements')
                        BEGIN
                            CREATE TABLE Supplements (
                                Id NVARCHAR(50) PRIMARY KEY,
                                Name NVARCHAR(255) NOT NULL,
                                Dose NVARCHAR(255),
                                Time NVARCHAR(255),
                                TimeGroup NVARCHAR(50)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create SessionSteps table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SessionSteps')
                        BEGIN
                            CREATE TABLE SessionSteps (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                DayType NVARCHAR(50) NOT NULL,
                                ExerciseId NVARCHAR(50) NOT NULL,
                                Phase1Rx NVARCHAR(255),
                                Phase2Rx NVARCHAR(255),
                                PhaseOnly INT NULL,
                                Section NVARCHAR(50),
                                SortOrder INT,
                                FOREIGN KEY (ExerciseId) REFERENCES Exercises(Id)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create DailyChecks table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'DailyChecks')
                        BEGIN
                            CREATE TABLE DailyChecks (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId NVARCHAR(50) NOT NULL,
                                Date NVARCHAR(10) NOT NULL,
                                ItemType NVARCHAR(50) NOT NULL,
                                ItemId NVARCHAR(50) NOT NULL,
                                StepIndex INT,
                                Checked BIT DEFAULT 0
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create Milestones table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'Milestones')
                        BEGIN
                            CREATE TABLE Milestones (
                                Id NVARCHAR(50) PRIMARY KEY,
                                Name NVARCHAR(255) NOT NULL,
                                Done BIT DEFAULT 0,
                                AchievedDate NVARCHAR(10)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create SessionLogs table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'SessionLogs')
                        BEGIN
                            CREATE TABLE SessionLogs (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                UserId NVARCHAR(50) NOT NULL,
                                Date NVARCHAR(10) NOT NULL,
                                StepsDone INT,
                                StepsTotal INT
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create UserSettings table
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserSettings')
                        BEGIN
                            CREATE TABLE UserSettings (
                                UserId NVARCHAR(50) PRIMARY KEY,
                                StartDate NVARCHAR(10),
                                DisabledTools NVARCHAR(MAX)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Create UserMilestones table (per-user milestone tracking)
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'UserMilestones')
                        BEGIN
                            CREATE TABLE UserMilestones (
                                UserId NVARCHAR(100) NOT NULL,
                                MilestoneId NVARCHAR(50) NOT NULL,
                                Done BIT DEFAULT 0,
                                AchievedDate NVARCHAR(10),
                                PRIMARY KEY (UserId, MilestoneId)
                            )
                        END";
                    await command.ExecuteNonQueryAsync();
                }

                // Seed Exercises
                await SeedExercisesAsync(connection);

                // Seed Supplements
                await SeedSupplementsAsync(connection);

                // Seed Milestones
                await SeedMilestonesAsync(connection);

                // Seed SessionSteps
                await SeedSessionStepsAsync(connection);
            }
        }

        public async Task InitializeAsync()
        {
            await EnsureTablesExistAsync();
        }

        private async Task SeedExercisesAsync(SqlConnection connection)
        {
            var exercises = new List<(string id, string name, string category, string targets, string description, string cues, string explanation, string warning, string phases)>
            {
                ("hot-tub", "Hot Tub", "warmup_tool", "Full Body", "Warm up in hot water to increase circulation", "2-5 min | No excessive heat", "Heat increases tissue elasticity and blood flow", "Avoid if pregnant or have heart conditions", "1,2"),
                ("vibration-plate", "Vibration Plate", "warmup_tool", "Full Body", "Stand on vibration plate for neuromuscular activation", "Feet hip-width | Slight knee bend | 30-60 sec", "Vibration activates muscles and improves proprioception", "Not for acute injuries or pregnancy", "1,2"),
                ("cars-routine", "CARs Routine", "mobility", "All Joints", "Controlled Articular Rotations for joint mobility", "Slow circles | Full range | No momentum", "CARs improve joint health and body awareness", "Move within pain-free range only", "1,2"),
                ("90-90-hip-switch", "90/90 Hip Switch", "mobility", "Hips", "Switch legs in 90/90 position for hip mobility", "Chest upright | Slow switch | 30 sec each side", "Improves hip external and internal rotation", "Stop if sharp pain occurs", "1,2"),
                ("shinbox-getup", "Shinbox Get-Up", "mobility", "Hips,Core", "Get up from shinbox position without using hands", "Controlled movement | No hand assist | 5 reps", "Builds hip mobility and core stability", "Requires significant hip mobility", "2"),
                ("worlds-greatest-stretch", "World's Greatest Stretch", "mobility", "Hips,T-Spine,Ankles", "Dynamic stretch targeting multiple areas", "Lunge | Rotate | Reach | 8-10 reps each side", "Comprehensive dynamic mobility for warm-up", "Avoid with acute injuries", "1,2"),
                ("deep-squat-hold", "Deep Squat Hold", "mobility", "Hips,Knees,Ankles", "Hold bottom of squat position", "Feet shoulder-width | Upright torso | Hold 30-60 sec", "Improves squat mechanics and ankle mobility", "Use support if needed for balance", "1,2"),
                ("couch-stretch", "Couch Stretch", "mobility", "Hips,Quads", "Quad stretch on couch or box", "Back knee elevated | Gentle forward lean | 90 sec each", "Improves hip and quad flexibility", "Can be intense; progress gradually", "1,2"),
                ("wall-ankle-mob", "Wall Ankle Mobilization", "mobility", "Ankles", "Mobilize ankle with wall for dorsiflexion", "Shin against wall | Lean forward | 90 sec each", "Improves ankle dorsiflexion and calf mobility", "Stop if pain in ankle", "1,2"),
                ("open-book", "Open Book (T-Spine)", "mobility", "T-Spine", "Thoracic spine rotation from side-lying", "Side-lying | Controlled rotation | 10 reps each side", "Improves thoracic rotation and spinal mobility", "Avoid jerky movements", "1,2"),
                ("dead-hang", "Dead Hang", "mobility", "Shoulders,Spine,Grip", "Hang from bar with full body relaxed", "Full grip | Shoulders engaged | 20-60 sec", "Decompresses spine and improves shoulder mobility", "Build up duration gradually", "1,2"),
                ("quadruped-rocking", "Quadruped Rocking", "mobility", "Hips,Ankles", "Rock back and forth on hands and knees", "Hands under shoulders | Slow rocks | 20 reps", "Improves hip and ankle mobility", "Keep core engaged", "1,2"),
                ("hip-flexor-pails-rails", "Hip Flexor PAILs/RAILs", "mobility", "Hips", "Proprioceptive stretching for hip flexors", "Hold position | Contract | Relax | 8-10 reps", "Increases hip flexor mobility and stability", "Requires space and understanding of PAILs/RAILs", "2"),
                ("90-90-pails-rails", "90/90 PAILs/RAILs", "mobility", "Hips", "Proprioceptive stretching in 90/90 position", "Hold | Contract | Relax | 8-10 reps each side", "Improves hip external rotation", "Advanced technique; build foundation first", "2"),
                ("ankle-pails-rails", "Ankle PAILs/RAILs", "mobility", "Ankles", "Proprioceptive stretching for ankle mobility", "Hold position | Contract | Relax | 8-10 reps", "Improves ankle dorsiflexion and control", "Requires proprioceptive understanding", "2"),
                ("goblet-squat", "Goblet Squat (3s Pause)", "strength", "Hips,Knees,Core", "Squat while holding weight with 3-second pause", "Hold weight at chest | Deep squat | Pause at bottom | 8 reps", "Builds squat strength and depth", "Start with light weight", "2"),
                ("turkish-getup", "Turkish Get-Up", "strength", "Full Body", "Get up from lying to standing while holding weight", "Controlled movement | Full attention | 5 reps each side", "Full body strength and stability", "Complex movement; practice without weight first", "2"),
                ("cossack-squat", "Cossack Squat", "strength", "Hips,Knees,Ankles", "Shift side to side in wide squat stance", "Wide stance | Shift weight | Touch floor | 8 reps each", "Builds lateral hip and knee strength", "Requires significant mobility", "2"),
                ("jefferson-curl", "Jefferson Curl", "strength", "Spine,Hamstrings", "Curl spine vertebra by vertebra", "Standing | Slow curl | Articulate spine | 8-10 reps", "Improves spinal flexion and hamstring flexibility", "Go slow to avoid injury", "2"),
                ("hydro-massager", "Hydro Massager", "recovery_tool", "Full Body", "Use hydro massager for muscle recovery", "Various speeds | Target muscles | 5-10 min", "Increases circulation and aids recovery", "Avoid over-sensitive areas", "1,2"),
                ("steam-sauna", "Steam Sauna", "recovery_tool", "Full Body", "Relax in steam sauna for recovery", "10-20 min | Stay hydrated | Moderate temperature", "Promotes relaxation and circulation", "Avoid if pregnant or have heart conditions", "1,2"),
                ("dry-sauna", "Dry Sauna", "recovery_tool", "Full Body", "Relax in dry sauna for muscle recovery", "10-20 min | Stay hydrated | Moderate temperature", "Reduces muscle soreness and promotes recovery", "Stay well hydrated", "1,2"),
                ("compex-warmup", "Compex — Warmup", "recovery_tool", "Quads,Glutes", "Use Compex muscle stimulator for warm-up", "Warmup setting | 10-15 min | Quads and glutes", "Prepares muscles for training", "Follow device instructions", "1,2"),
                ("compex-recovery", "Compex — Recovery", "recovery_tool", "Quads,Glutes,Calves", "Use Compex for post-workout recovery", "Recovery setting | 15-20 min | Multiple muscles", "Aids muscle recovery and reduces soreness", "Follow device instructions", "1,2"),
                ("compression-boots", "Compression Boots", "recovery_tool", "Legs", "Use compression boots for leg recovery", "15-30 min | Moderate compression | Legs only", "Improves circulation and reduces leg soreness", "Start with shorter durations", "1,2")
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    MERGE INTO Exercises AS target
                    USING (VALUES
                        (@id, @name, @category, @targets, @description, @cues, @explanation, @warning, @phases)
                    ) AS source (Id, Name, Category, Targets, Description, Cues, Explanation, Warning, Phases)
                    ON target.Id = source.Id
                    WHEN NOT MATCHED THEN
                        INSERT (Id, Name, Category, Targets, Description, Cues, Explanation, Warning, Phases)
                        VALUES (source.Id, source.Name, source.Category, source.Targets, source.Description, source.Cues, source.Explanation, source.Warning, source.Phases);";

                foreach (var exercise in exercises)
                {
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@id", exercise.id);
                    command.Parameters.AddWithValue("@name", exercise.name);
                    command.Parameters.AddWithValue("@category", exercise.category);
                    command.Parameters.AddWithValue("@targets", exercise.targets);
                    command.Parameters.AddWithValue("@description", exercise.description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@cues", exercise.cues ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@explanation", exercise.explanation ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@warning", exercise.warning ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@phases", exercise.phases);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task SeedSupplementsAsync(SqlConnection connection)
        {
            var supplements = new List<(string id, string name, string dose, string time, string timeGroup)>
            {
                ("supp-collagen", "Collagen + Vitamin C", "10-15g + 50mg C", "AM / Pre-workout", "am"),
                ("supp-omega3", "Omega-3 Fish Oil", "~1500mg EPA+DHA", "AM with food", "am"),
                ("supp-vitamind", "Vitamin D3 + K2", "2000-4000 IU + 100mcg K2", "AM with food", "am"),
                ("supp-creatine", "Creatine Monohydrate", "3-5g", "AM", "am"),
                ("supp-curcumin", "Curcumin (w/ piperine)", "500-1500mg", "With lunch", "mid"),
                ("supp-omega3b", "Omega-3 (2nd dose)", "~1500mg EPA+DHA", "PM with dinner", "pm"),
                ("supp-mag", "Magnesium Glycinate", "300-400mg", "Before bed", "pm")
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    MERGE INTO Supplements AS target
                    USING (VALUES
                        (@id, @name, @dose, @time, @timeGroup)
                    ) AS source (Id, Name, Dose, Time, TimeGroup)
                    ON target.Id = source.Id
                    WHEN NOT MATCHED THEN
                        INSERT (Id, Name, Dose, Time, TimeGroup)
                        VALUES (source.Id, source.Name, source.Dose, source.Time, source.TimeGroup);";

                foreach (var supplement in supplements)
                {
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@id", supplement.id);
                    command.Parameters.AddWithValue("@name", supplement.name);
                    command.Parameters.AddWithValue("@dose", supplement.dose ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@time", supplement.time ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@timeGroup", supplement.timeGroup);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task SeedMilestonesAsync(SqlConnection connection)
        {
            var milestones = new List<(string id, string name)>
            {
                ("kneel", "Kneel without discomfort"),
                ("squat60", "Deep squat hold — 60 sec"),
                ("squat120", "Deep squat hold — 2 min"),
                ("hang30", "Dead hang — 30 sec"),
                ("hang60", "Dead hang — 60 sec"),
                ("shinbox", "Shinbox get-up without hands"),
                ("tgu-kb", "Turkish get-up with KB"),
                ("cossack-floor", "Cossack squat — touch floor"),
                ("floor-nohand", "Floor to standing — no hands")
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    MERGE INTO Milestones AS target
                    USING (VALUES
                        (@id, @name)
                    ) AS source (Id, Name)
                    ON target.Id = source.Id
                    WHEN NOT MATCHED THEN
                        INSERT (Id, Name, Done)
                        VALUES (source.Id, source.Name, 0);";

                foreach (var milestone in milestones)
                {
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@id", milestone.id);
                    command.Parameters.AddWithValue("@name", milestone.name);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        private async Task SeedSessionStepsAsync(SqlConnection connection)
        {
            var steps = new List<(string dayType, string exerciseId, string phase1Rx, string phase2Rx, int? phaseOnly, string section, int sortOrder)>
            {
                // GYM sessions
                ("gym", "hot-tub", "5 min", "5 min", null, "warmup", 1),
                ("gym", "vibration-plate", "1 min", "1 min", null, "warmup", 2),
                ("gym", "cars-routine", "10 min", "10 min", null, "warmup", 3),
                ("gym", "deep-squat-hold", "60 sec", "90 sec", null, "mobility", 4),
                ("gym", "dead-hang", "30 sec x3", "60 sec x3", null, "mobility", 5),
                ("gym", "world-greatest-stretch", "8 reps each", "10 reps each", null, "mobility", 6),
                ("gym", "goblet-squat", null, "8 reps x3", 2, "strength", 7),
                ("gym", "turkish-getup", null, "5 reps each x2", 2, "strength", 8),
                ("gym", "hydro-massager", "5 min", "5 min", null, "recovery", 9),
                ("gym", "compex-recovery", "15 min", "15 min", null, "recovery", 10),

                // HOME sessions
                ("home", "cars-routine", "5 min", "5 min", null, "mobility", 1),
                ("home", "90-90-hip-switch", "30 sec each", "30 sec each", null, "mobility", 2),
                ("home", "couch-stretch", "90 sec each", "90 sec each", null, "mobility", 3),
                ("home", "wall-ankle-mob", "90 sec each", "90 sec each", null, "mobility", 4),
                ("home", "open-book", "10 reps each", "10 reps each", null, "mobility", 5),
                ("home", "quadruped-rocking", "20 reps", "20 reps", null, "mobility", 6),
                ("home", "90-90-pails-rails", null, "8 reps each", 2, "mobility", 7),
                ("home", "hip-flexor-pails-rails", null, "8 reps", 2, "mobility", 8),
                ("home", "ankle-pails-rails", null, "8 reps", 2, "mobility", 9),
                ("home", "cossack-squat", null, "8 reps each", 2, "strength", 10),
                ("home", "jefferson-curl", null, "8 reps", 2, "strength", 11),

                // RECOVERY sessions
                ("recovery", "steam-sauna", "15 min", "15 min", null, "recovery", 1),
                ("recovery", "dry-sauna", "15 min", "15 min", null, "recovery", 2),
                ("recovery", "compression-boots", "20 min", "20 min", null, "recovery", 3),
                ("recovery", "hydro-massager", "10 min", "10 min", null, "recovery", 4),
                ("recovery", "compex-warmup", "10 min", "10 min", null, "recovery", 5),

                // REST sessions (typically just supplements and light mobility)
                ("rest", "cars-routine", "5 min", "5 min", null, "mobility", 1),
                ("rest", "dead-hang", "20 sec", "20 sec", null, "mobility", 2),
                ("rest", "deep-squat-hold", "30 sec", "30 sec", null, "mobility", 3)
            };

            using (var command = connection.CreateCommand())
            {
                command.CommandText = @"
                    IF NOT EXISTS (SELECT 1 FROM SessionSteps)
                    BEGIN
                        INSERT INTO SessionSteps (DayType, ExerciseId, Phase1Rx, Phase2Rx, PhaseOnly, Section, SortOrder)
                        VALUES (@dayType, @exerciseId, @phase1Rx, @phase2Rx, @phaseOnly, @section, @sortOrder)
                    END";

                foreach (var step in steps)
                {
                    command.Parameters.Clear();
                    command.Parameters.AddWithValue("@dayType", step.dayType);
                    command.Parameters.AddWithValue("@exerciseId", step.exerciseId);
                    command.Parameters.AddWithValue("@phase1Rx", step.phase1Rx ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@phase2Rx", step.phase2Rx ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@phaseOnly", step.phaseOnly ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@section", step.section ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@sortOrder", step.sortOrder);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Gets all exercises from the database.
        /// </summary>
        public async Task<List<Exercise>> GetAllExercisesAsync()
        {
            var exercises = new List<Exercise>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, Name, Category, Targets, Description, Cues, Explanation, Warning, Phases FROM Exercises ORDER BY Name";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            exercises.Add(new Exercise
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Category = reader.GetString(2),
                                Targets = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                Cues = reader.IsDBNull(5) ? null : reader.GetString(5),
                                Explanation = reader.IsDBNull(6) ? null : reader.GetString(6),
                                Warning = reader.IsDBNull(7) ? null : reader.GetString(7),
                                Phases = reader.GetString(8)
                            });
                        }
                    }
                }
            }

            return exercises;
        }

        /// <summary>
        /// Gets a single exercise by ID.
        /// </summary>
        public async Task<Exercise> GetExerciseAsync(string id)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, Name, Category, Targets, Description, Cues, Explanation, Warning, Phases FROM Exercises WHERE Id = @id";
                    command.Parameters.AddWithValue("@id", id);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new Exercise
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Category = reader.GetString(2),
                                Targets = reader.GetString(3),
                                Description = reader.IsDBNull(4) ? null : reader.GetString(4),
                                Cues = reader.IsDBNull(5) ? null : reader.GetString(5),
                                Explanation = reader.IsDBNull(6) ? null : reader.GetString(6),
                                Warning = reader.IsDBNull(7) ? null : reader.GetString(7),
                                Phases = reader.GetString(8)
                            };
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets all supplements.
        /// </summary>
        public async Task<List<Supplement>> GetSupplementsAsync()
        {
            var supplements = new List<Supplement>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, Name, Dose, Time, TimeGroup FROM Supplements ORDER BY TimeGroup, Name";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            supplements.Add(new Supplement
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Dose = reader.IsDBNull(2) ? null : reader.GetString(2),
                                Time = reader.IsDBNull(3) ? null : reader.GetString(3),
                                TimeGroup = reader.GetString(4)
                            });
                        }
                    }
                }
            }

            return supplements;
        }

        /// <summary>
        /// Gets all session steps for a specific day type.
        /// </summary>
        public async Task<List<SessionStep>> GetSessionStepsAsync(string dayType)
        {
            var steps = new List<SessionStep>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, DayType, ExerciseId, Phase1Rx, Phase2Rx, PhaseOnly, Section, SortOrder
                        FROM SessionSteps
                        WHERE DayType = @dayType
                        ORDER BY SortOrder";
                    command.Parameters.AddWithValue("@dayType", dayType);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            steps.Add(new SessionStep
                            {
                                Id = reader.GetInt32(0),
                                DayType = reader.GetString(1),
                                ExerciseId = reader.GetString(2),
                                Phase1Rx = reader.IsDBNull(3) ? null : reader.GetString(3),
                                Phase2Rx = reader.IsDBNull(4) ? null : reader.GetString(4),
                                PhaseOnly = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                                Section = reader.IsDBNull(6) ? null : reader.GetString(6),
                                SortOrder = reader.GetInt32(7)
                            });
                        }
                    }
                }
            }

            return steps;
        }

        /// <summary>
        /// Gets all daily checks for a specific user and date.
        /// </summary>
        public async Task<List<DailyCheck>> GetDailyChecksAsync(string userId, string date)
        {
            var checks = new List<DailyCheck>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Date, ItemType, ItemId, StepIndex, Checked
                        FROM DailyChecks
                        WHERE UserId = @userId AND Date = @date
                        ORDER BY ItemType, ItemId, StepIndex";
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@date", date);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            checks.Add(new DailyCheck
                            {
                                Id = reader.GetInt32(0),
                                UserId = reader.GetString(1),
                                Date = reader.GetString(2),
                                ItemType = reader.GetString(3),
                                ItemId = reader.GetString(4),
                                StepIndex = reader.IsDBNull(5) ? null : reader.GetInt32(5),
                                Checked = reader.GetBoolean(6)
                            });
                        }
                    }
                }
            }

            return checks;
        }

        /// <summary>
        /// Toggles a check for a specific daily item.
        /// </summary>
        public async Task ToggleCheckAsync(string userId, string date, string itemType, string itemId, int stepIndex)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF EXISTS (SELECT 1 FROM DailyChecks WHERE UserId = @userId AND Date = @date AND ItemType = @itemType AND ItemId = @itemId AND StepIndex = @stepIndex)
                        BEGIN
                            UPDATE DailyChecks
                            SET Checked = CASE WHEN Checked = 1 THEN 0 ELSE 1 END
                            WHERE UserId = @userId AND Date = @date AND ItemType = @itemType AND ItemId = @itemId AND StepIndex = @stepIndex
                        END
                        ELSE
                        BEGIN
                            INSERT INTO DailyChecks (UserId, Date, ItemType, ItemId, StepIndex, Checked)
                            VALUES (@userId, @date, @itemType, @itemId, @stepIndex, 1)
                        END";

                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@date", date);
                    command.Parameters.AddWithValue("@itemType", itemType);
                    command.Parameters.AddWithValue("@itemId", itemId);
                    command.Parameters.AddWithValue("@stepIndex", stepIndex);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Gets user settings or creates default if not exists.
        /// </summary>
        public async Task<UserSettings> GetUserSettingsAsync(string userId)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT UserId, StartDate, DisabledTools FROM UserSettings WHERE UserId = @userId";
                    command.Parameters.AddWithValue("@userId", userId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new UserSettings
                            {
                                UserId = reader.GetString(0),
                                StartDate = reader.IsDBNull(1) ? null : reader.GetString(1),
                                DisabledTools = reader.IsDBNull(2) ? null : reader.GetString(2)
                            };
                        }
                    }
                }

                // Create default settings if not exists
                var defaultSettings = new UserSettings { UserId = userId, StartDate = null, DisabledTools = null };
                await SaveUserSettingsAsync(defaultSettings);
                return defaultSettings;
            }
        }

        /// <summary>
        /// Saves or updates user settings.
        /// </summary>
        public async Task SaveUserSettingsAsync(UserSettings settings)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        MERGE INTO UserSettings AS target
                        USING (VALUES (@userId, @startDate, @disabledTools)) AS source (UserId, StartDate, DisabledTools)
                        ON target.UserId = source.UserId
                        WHEN MATCHED THEN
                            UPDATE SET StartDate = @startDate, DisabledTools = @disabledTools
                        WHEN NOT MATCHED THEN
                            INSERT (UserId, StartDate, DisabledTools)
                            VALUES (@userId, @startDate, @disabledTools);";

                    command.Parameters.AddWithValue("@userId", settings.UserId);
                    command.Parameters.AddWithValue("@startDate", settings.StartDate ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@disabledTools", settings.DisabledTools ?? (object)DBNull.Value);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Gets all milestones.
        /// </summary>
        public async Task<List<Milestone>> GetMilestonesAsync()
        {
            var milestones = new List<Milestone>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, Name, Done, AchievedDate FROM Milestones ORDER BY Name";

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            milestones.Add(new Milestone
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Done = reader.GetBoolean(2),
                                AchievedDate = reader.IsDBNull(3) ? null : reader.GetString(3)
                            });
                        }
                    }
                }
            }

            return milestones;
        }

        /// <summary>
        /// Marks a milestone as completed.
        /// </summary>
        public async Task CompleteMilestoneAsync(string id, string date)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        UPDATE Milestones
                        SET Done = 1, AchievedDate = @date
                        WHERE Id = @id";

                    command.Parameters.AddWithValue("@id", id);
                    command.Parameters.AddWithValue("@date", date);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Gets all session logs for a specific user.
        /// </summary>
        public async Task<List<SessionLog>> GetSessionLogsAsync(string userId)
        {
            var logs = new List<SessionLog>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Date, StepsDone, StepsTotal
                        FROM SessionLogs
                        WHERE UserId = @userId
                        ORDER BY Date DESC";

                    command.Parameters.AddWithValue("@userId", userId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            logs.Add(new SessionLog
                            {
                                Id = reader.GetInt32(0),
                                UserId = reader.GetString(1),
                                Date = reader.GetString(2),
                                StepsDone = reader.GetInt32(3),
                                StepsTotal = reader.GetInt32(4)
                            });
                        }
                    }
                }
            }

            return logs;
        }

        /// <summary>
        /// Logs a session for a specific user and date. Inserts if not exists for that date.
        /// </summary>
        public async Task LogSessionAsync(string userId, string date, int done, int total)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT 1 FROM SessionLogs WHERE UserId = @userId AND Date = @date)
                        BEGIN
                            INSERT INTO SessionLogs (UserId, Date, StepsDone, StepsTotal)
                            VALUES (@userId, @date, @stepsDone, @stepsTotal)
                        END
                        ELSE
                        BEGIN
                            UPDATE SessionLogs
                            SET StepsDone = @stepsDone, StepsTotal = @stepsTotal
                            WHERE UserId = @userId AND Date = @date
                        END";

                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@date", date);
                    command.Parameters.AddWithValue("@stepsDone", done);
                    command.Parameters.AddWithValue("@stepsTotal", total);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Gets the session log for a specific user and date.
        /// </summary>
        public async Task<SessionLog> GetSessionLogForDateAsync(string userId, string date)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, UserId, Date, StepsDone, StepsTotal
                        FROM SessionLogs
                        WHERE UserId = @userId AND Date = @date";

                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@date", date);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            return new SessionLog
                            {
                                Id = reader.GetInt32(0),
                                UserId = reader.GetString(1),
                                Date = reader.GetString(2),
                                StepsDone = reader.GetInt32(3),
                                StepsTotal = reader.GetInt32(4)
                            };
                        }
                    }
                }
            }

            return null;
        }

        // ============ AUTH METHODS ============

        public async Task<AppUser?> CreateUserAsync(string username, string password)
        {
            // Generate salt and hash
            byte[] saltBytes = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
            {
                rng.GetBytes(saltBytes);
            }
            string salt = Convert.ToBase64String(saltBytes);
            string hash = HashPassword(password, salt);
            string today = DateTime.UtcNow.ToString("yyyy-MM-dd");

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        IF NOT EXISTS (SELECT 1 FROM Users WHERE Username = @username)
                        BEGIN
                            INSERT INTO Users (Username, PasswordHash, Salt, CreatedDate)
                            OUTPUT INSERTED.Id
                            VALUES (@username, @hash, @salt, @today)
                        END";
                    command.Parameters.AddWithValue("@username", username);
                    command.Parameters.AddWithValue("@hash", hash);
                    command.Parameters.AddWithValue("@salt", salt);
                    command.Parameters.AddWithValue("@today", today);

                    var result = await command.ExecuteScalarAsync();
                    if (result == null) return null; // Username already exists

                    return new AppUser
                    {
                        Id = (int)result,
                        Username = username,
                        PasswordHash = hash,
                        Salt = salt,
                        CreatedDate = today
                    };
                }
            }
        }

        public async Task<AppUser?> ValidateUserAsync(string username, string password)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "SELECT Id, Username, PasswordHash, Salt, CreatedDate FROM Users WHERE Username = @username";
                    command.Parameters.AddWithValue("@username", username);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        if (await reader.ReadAsync())
                        {
                            var user = new AppUser
                            {
                                Id = reader.GetInt32(0),
                                Username = reader.GetString(1),
                                PasswordHash = reader.GetString(2),
                                Salt = reader.GetString(3),
                                CreatedDate = reader.GetString(4)
                            };

                            string hash = HashPassword(password, user.Salt);
                            if (hash == user.PasswordHash)
                                return user;
                        }
                    }
                }
            }
            return null;
        }

        private static string HashPassword(string password, string salt)
        {
            using (var pbkdf2 = new System.Security.Cryptography.Rfc2898DeriveBytes(
                password,
                Convert.FromBase64String(salt),
                100000,
                System.Security.Cryptography.HashAlgorithmName.SHA256))
            {
                byte[] hash = pbkdf2.GetBytes(32);
                return Convert.ToBase64String(hash);
            }
        }

        // ============ PER-USER MILESTONES ============

        public async Task<List<Milestone>> GetUserMilestonesAsync(string userId)
        {
            var milestones = new List<Milestone>();

            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT m.Id, m.Name, ISNULL(um.Done, 0) AS Done, um.AchievedDate
                        FROM Milestones m
                        LEFT JOIN UserMilestones um ON m.Id = um.MilestoneId AND um.UserId = @userId
                        ORDER BY m.Name";
                    command.Parameters.AddWithValue("@userId", userId);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            milestones.Add(new Milestone
                            {
                                Id = reader.GetString(0),
                                Name = reader.GetString(1),
                                Done = reader.GetBoolean(2),
                                AchievedDate = reader.IsDBNull(3) ? null : reader.GetString(3)
                            });
                        }
                    }
                }
            }

            return milestones;
        }

        public async Task CompleteUserMilestoneAsync(string userId, string milestoneId, string date)
        {
            using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        MERGE INTO UserMilestones AS target
                        USING (VALUES (@userId, @milestoneId)) AS source (UserId, MilestoneId)
                        ON target.UserId = source.UserId AND target.MilestoneId = source.MilestoneId
                        WHEN MATCHED THEN
                            UPDATE SET Done = 1, AchievedDate = @date
                        WHEN NOT MATCHED THEN
                            INSERT (UserId, MilestoneId, Done, AchievedDate)
                            VALUES (@userId, @milestoneId, 1, @date);";
                    command.Parameters.AddWithValue("@userId", userId);
                    command.Parameters.AddWithValue("@milestoneId", milestoneId);
                    command.Parameters.AddWithValue("@date", date);

                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
