package com.fitmate.backend;

import org.springframework.boot.SpringApplication;
import org.springframework.boot.autoconfigure.SpringBootApplication;

@SpringBootApplication
public class FitMateBackendApplication {

    public static void main(String[] args) {
        SpringApplication.run(FitMateBackendApplication.class, args);
        System.out.println("\n=======================================================");
        System.out.println("🚀 FitMate AI Backend Spring Boot is RUNNING on port 8080!");
        System.out.println("=======================================================\n");
    }
}
