package com.fitmate.backend.service;

import com.fitmate.backend.entity.PtProfile;
import com.fitmate.backend.entity.User;
import com.fitmate.backend.entity.UserProfile;
import com.fitmate.backend.repository.PtProfileRepository;
import com.fitmate.backend.repository.UserProfileRepository;
import com.fitmate.backend.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.stereotype.Service;

import java.util.HashMap;
import java.util.Map;
import java.util.Optional;

@Service
@RequiredArgsConstructor
public class UserService {

    private final UserRepository userRepository;
    private final UserProfileRepository userProfileRepository;
    private final PtProfileRepository ptProfileRepository;

    public Map<String, Object> getUserProfile(Long userId) {
        User user = userRepository.findById(userId)
                .orElseThrow(() -> new RuntimeException("Người dùng không tồn tại!"));

        Optional<UserProfile> profileOpt = userProfileRepository.findByUserId(userId);
        Optional<PtProfile> ptOpt = ptProfileRepository.findByUserId(userId);

        Map<String, Object> result = new HashMap<>();
        result.put("userId", user.getId());
        result.put("email", user.getEmail());
        result.put("name", user.getFullName());
        result.put("role", user.getRole().equals("PT") ? "Huấn luyện viên" : "Hội viên");
        result.put("isPt", user.getRole().equals("PT"));
        result.put("avatar", user.getAvatarUrl() != null ? user.getAvatarUrl() : 
            (user.getRole().equals("PT") 
                ? "https://images.unsplash.com/photo-1548690312-e3b507d8c110?w=150&auto=format&fit=crop&q=60"
                : "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?w=150&auto=format&fit=crop&q=60"));

        if (profileOpt.isPresent()) {
            UserProfile p = profileOpt.get();
            result.put("height", p.getHeight() != null ? p.getHeight() + " cm" : "175 cm");
            result.put("weight", p.getWeight() != null ? p.getWeight() + " kg" : "70 kg");
            result.put("gender", p.getGender() != null ? p.getGender() : "Nam");
            result.put("birthday", p.getBirthday() != null ? p.getBirthday() : "15/05/2004");
            result.put("fitnessGoal", p.getFitnessGoal() != null ? p.getFitnessGoal() : "Tăng cơ");
            result.put("allergies", p.getAllergies() != null ? p.getAllergies() : "Không có");
        }

        if (ptOpt.isPresent()) {
            PtProfile pt = ptOpt.get();
            result.put("spec", pt.getSpecialty() != null ? pt.getSpecialty().split(",") : new String[]{"Calisthenics"});
            result.put("exp", pt.getExperienceYears() != null ? pt.getExperienceYears() + " năm kinh nghiệm" : "3 năm kinh nghiệm");
            result.put("price", pt.getPricePerSession() != null ? pt.getPricePerSession() + "đ/buổi" : "300.000đ/buổi");
            result.put("certifications", pt.getCertifications());
        }

        return result;
    }
}
