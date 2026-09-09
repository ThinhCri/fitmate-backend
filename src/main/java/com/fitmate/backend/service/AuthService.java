package com.fitmate.backend.service;

import com.fitmate.backend.dto.AuthResponse;
import com.fitmate.backend.dto.LoginRequest;
import com.fitmate.backend.dto.RegisterRequest;
import com.fitmate.backend.entity.PtProfile;
import com.fitmate.backend.entity.User;
import com.fitmate.backend.entity.UserProfile;
import com.fitmate.backend.repository.PtProfileRepository;
import com.fitmate.backend.repository.UserProfileRepository;
import com.fitmate.backend.repository.UserRepository;
import lombok.RequiredArgsConstructor;
import org.springframework.security.crypto.password.PasswordEncoder;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

import java.util.Optional;
import java.util.UUID;

@Service
@RequiredArgsConstructor
public class AuthService {

    private final UserRepository userRepository;
    private final UserProfileRepository userProfileRepository;
    private final PtProfileRepository ptProfileRepository;
    private final PasswordEncoder passwordEncoder;

    @Transactional
    public AuthResponse register(RegisterRequest request) {
        if (userRepository.existsByEmail(request.getEmail())) {
            throw new RuntimeException("Email đã tồn tại trên hệ thống! Vui lòng đăng nhập hoặc dùng email khác.");
        }

        String userRole = (request.getRole() != null && request.getRole().equalsIgnoreCase("pt")) ? "PT" : "USER";

        // Save User with BCrypt Encrypted Password
        User user = User.builder()
                .email(request.getEmail())
                .password(passwordEncoder.encode(request.getPassword())) // BCrypt hashing!
                .fullName(request.getName() != null ? request.getName() : "Người dùng FitMate")
                .role(userRole)
                .build();

        User savedUser = userRepository.save(user);

        // Save User Profile
        UserProfile profile = UserProfile.builder()
                .user(savedUser)
                .height(request.getHeight())
                .weight(request.getWeight())
                .gender(request.getGender())
                .birthday(request.getBirthday())
                .fitnessGoal(request.getGoal())
                .selectedDays(request.getSelectedDays())
                .build();
        userProfileRepository.save(profile);

        // If PT, save PT Profile
        if ("PT".equals(userRole)) {
            PtProfile ptProfile = PtProfile.builder()
                    .user(savedUser)
                    .specialty(request.getPtSpec())
                    .experienceYears(request.getPtExp())
                    .pricePerSession(request.getPtPrice())
                    .certifications(request.getPtCerts())
                    .rating(5.0)
                    .isVerified(true)
                    .build();
            ptProfileRepository.save(ptProfile);
        }

        String token = "FITMATE-JWT-" + UUID.randomUUID().toString();

        return AuthResponse.builder()
                .token(token)
                .userId(savedUser.getId())
                .email(savedUser.getEmail())
                .name(savedUser.getFullName())
                .role(savedUser.getRole())
                .message("Đăng ký tài khoản thành công! 🎉 (Mật khẩu đã mã hóa BCrypt)")
                .build();
    }

    public AuthResponse login(LoginRequest request) {
        Optional<User> userOpt = userRepository.findByEmail(request.getEmail());

        if (userOpt.isEmpty()) {
            throw new RuntimeException("Tài khoản không tồn tại! Vui lòng kiểm tra lại email.");
        }

        User user = userOpt.get();

        // Check password using BCrypt matches
        if (!passwordEncoder.matches(request.getPassword(), user.getPassword())) {
            throw new RuntimeException("Mật khẩu không chính xác! ⚠️");
        }

        String token = "FITMATE-JWT-" + UUID.randomUUID().toString();

        return AuthResponse.builder()
                .token(token)
                .userId(user.getId())
                .email(user.getEmail())
                .name(user.getFullName())
                .role(user.getRole())
                .message("Đăng nhập thành công!")
                .build();
    }
}
