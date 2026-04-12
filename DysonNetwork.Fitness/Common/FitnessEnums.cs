namespace DysonNetwork.Fitness;

public enum WorkoutType
{
    Strength,
    Cardio,
    Flexibility,
    Mixed,
    Other
}

public enum FitnessMetricType
{
    Weight,
    Height,
    Steps,
    HeartRate,
    BloodPressure,
    Sleep,
    BodyFat,
    WaterIntake,
    Calories,
    Distance,
    Duration,
    Custom
}

public enum FitnessGoalType
{
    WeightLoss,
    WeightGain,
    Steps,
    Distance,
    Duration,
    Reps,
    Strength,
    Cardio,
    Flexibility,
    Custom
}

public enum FitnessGoalStatus
{
    Active,
    Completed,
    Paused,
    Cancelled
}

public enum RepeatType
{
    None = 0,
    Daily = 1,
    Weekly = 2,
    Monthly = 3
}

public enum FitnessVisibility
{
    Private = 0,
    Public = 1
}

public enum LeaderboardType
{
    Calories = 0,
    Workouts = 1,
    Goals = 2,
    Distance = 3
}

public enum LeaderboardPeriod
{
    Daily = 0,
    Weekly = 1,
    Monthly = 2,
    AllTime = 3
}

public enum ExerciseCategory
{
    Cardio,
    Strength,
    Flexibility,
    Balance,
    Other
}

public enum DifficultyLevel
{
    Beginner,
    Intermediate,
    Advanced
}
