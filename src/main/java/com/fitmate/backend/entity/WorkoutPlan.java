package com.fitmate.backend.entity;

import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "workout_plans")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class WorkoutPlan {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "user_id", nullable = false)
    private User user;

    private String dayOfWeek; // 'Th 2', 'Th 3'...
    private String workoutName;
    private Integer durationMinutes;
    private Integer targetCaloriesBurn;
    private Boolean isCompleted;
}
