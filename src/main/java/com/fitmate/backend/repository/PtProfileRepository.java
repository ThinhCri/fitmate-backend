package com.fitmate.backend.repository;

import com.fitmate.backend.entity.PtProfile;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.stereotype.Repository;

import java.util.Optional;

@Repository
public interface PtProfileRepository extends JpaRepository<PtProfile, Long> {
    Optional<PtProfile> findByUserId(Long userId);
}
