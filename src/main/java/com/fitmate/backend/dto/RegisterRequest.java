package com.fitmate.backend.dto;

import lombok.Data;

@Data
public class RegisterRequest {
    private String email;
    private String password;
    private String name;
    private String role; // "user" or "pt"
    
    // User Profile fields
    private Double height;
    private Double weight;
    private String gender;
    private String birthday;
    private String goal;
    private String selectedDays;

    // PT Profile fields
    private String ptSpec;
    private String ptExp;
    private Double ptPrice;
    private String ptCerts;
}
