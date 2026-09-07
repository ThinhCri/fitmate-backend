package com.fitmate.backend.entity;

import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "meal_schedules")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class MealSchedule {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "user_id", nullable = false)
    private User user;

    private String dayOfWeek; // 'Th 2', 'Th 3'...
    private String mealType;  // 'breakfast', 'lunch', 'dinner'
    private String foodName;
    private Integer calories;
    private Integer protein;
    private Integer carbs;
    private Integer fat;
}
