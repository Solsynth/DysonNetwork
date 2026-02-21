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
