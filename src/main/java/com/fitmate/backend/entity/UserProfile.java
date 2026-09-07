package com.fitmate.backend.entity;

import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "user_profiles")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class UserProfile {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "user_id", nullable = false)
    private User user;

    private Double height;
    private Double weight;
    private String gender;
    private String birthday;
    private String fitnessGoal;

    @Column(columnDefinition = "TEXT")
    private String selectedDays; // JSON string e.g. ["Thứ 2", "Thứ 4"]

    @Column(columnDefinition = "TEXT")
    private String allergies; // Dị ứng thực phẩm

    private String weightGoal; // 'lose', 'maintain', 'gain'
}
