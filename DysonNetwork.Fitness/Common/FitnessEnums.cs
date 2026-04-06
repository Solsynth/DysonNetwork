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
