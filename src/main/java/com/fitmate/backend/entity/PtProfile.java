package com.fitmate.backend.entity;

import jakarta.persistence.*;
import lombok.*;

@Entity
@Table(name = "pt_profiles")
@Getter
@Setter
@NoArgsConstructor
@AllArgsConstructor
@Builder
public class PtProfile {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @OneToOne(fetch = FetchType.LAZY)
    @JoinColumn(name = "user_id", nullable = false)
    private User user;

    private String specialty;
    private String experienceYears;
    private Double pricePerSession;

    @Column(columnDefinition = "TEXT")
    private String certifications;

    private Double rating;
    private Boolean isVerified;
}
