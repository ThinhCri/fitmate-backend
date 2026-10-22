#!/bin/bash
# Fitmate VPS Initial Setup Script
# Run on a fresh Ubuntu 22.04 VPS as root
#
# Usage: ssh root@<VPS_IP> 'bash -s' < vps-setup.sh

set -euo pipefail

echo "=== Fitmate VPS Setup ==="
echo ""

# ---- 1. System update ----
echo "[1/6] Updating system..."
apt update && apt upgrade -y -q

# ---- 2. Install Docker ----
echo "[2/6] Installing Docker..."
if ! command -v docker &> /dev/null; then
    curl -fsSL https://get.docker.com -o /tmp/get-docker.sh
    sh /tmp/get-docker.sh
    rm /tmp/get-docker.sh
else
    echo "  Docker already installed: $(docker --version)"
fi

# Install Docker Compose plugin if not present
if ! docker compose version &> /dev/null; then
    apt install -y docker-compose-plugin
fi
echo "  Docker Compose: $(docker compose version)"

# ---- 3. Install Nginx ----
echo "[3/6] Installing Nginx..."
apt install -y nginx

# ---- 4. Install Certbot ----
echo "[4/6] Installing Certbot (Let's Encrypt)..."
apt install -y certbot python3-certbot-nginx

# ---- 5. Create fitmate user ----
echo "[5/6] Creating fitmate user..."
if ! id "fitmate" &>/dev/null; then
    adduser --disabled-password --gecos "Fitmate Service" fitmate
    usermod -aG docker fitmate
    usermod -aG sudo fitmate
    echo "  User 'fitmate' created and added to docker,sudo groups"
else
    echo "  User 'fitmate' already exists"
fi

# ---- 6. Create project directories ----
echo "[6/6] Creating project directories..."
su - fitmate -c '
    mkdir -p ~/fitmate/backend
    mkdir -p ~/fitmate/admin-web
    mkdir -p ~/fitmate/privacy
    echo "  Created ~/fitmate/{backend,admin-web,privacy}"
'

# ---- Firewall ----
echo ""
echo "[Bonus] Configuring UFW firewall..."
ufw allow OpenSSH
ufw allow 'Nginx Full'
ufw --force enable
ufw status

echo ""
echo "=== VPS Setup Complete ==="
echo ""
echo "Next steps:"
echo "  1. Upload Docker images:  scp fitmate-api.tar.gz fitmate@$(hostname -I | awk '{print $1}'):~/fitmate/backend/"
echo "  2. Upload admin-web:      scp admin-web-dist.tar.gz fitmate@$(hostname -I | awk '{print $1}'):~/fitmate/admin-web/"
echo "  3. Upload privacy page:   scp privacy-policy.html fitmate@$(hostname -I | awk '{print $1}'):~/fitmate/privacy/index.html"
echo "  4. SSH as fitmate:        ssh fitmate@$(hostname -I | awk '{print $1}')"
echo "  5. Follow deployment-guide.md from Giai Đoạn 3"
echo ""
