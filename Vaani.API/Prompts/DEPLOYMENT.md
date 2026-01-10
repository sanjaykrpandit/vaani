# Vaani API - AWS Deployment Guide

Complete guide for deploying Vaani API to AWS.

## ?? Prerequisites

- AWS Account
- AWS CLI installed and configured
- .NET 8 SDK
- Docker (for ECS deployment)
- PostgreSQL client tools

## ?? Deployment Options

### Option 1: AWS Elastic Beanstalk (Recommended)
**Best for:** Easy deployment, managed infrastructure, auto-scaling

### Option 2: AWS ECS with Fargate
**Best for:** Container-based deployment, better control

### Option 3: AWS EC2
**Best for:** Full control, custom configurations

---

## ?? Option 1: AWS Elastic Beanstalk Deployment

### Step 1: Install EB CLI

```bash
pip install awsebcli --upgrade --user
eb --version
```

### Step 2: Prepare Application

```bash
cd Vaani.API
dotnet publish -c Release -o publish
```

### Step 3: Initialize Elastic Beanstalk

```bash
eb init

# Select:
# - Region: us-east-1 (or your preferred region)
# - Application name: vaani-api
# - Platform: .NET Core on Linux
# - Platform version: .NET 8
# - SSH: Yes (optional)
```

### Step 4: Create RDS PostgreSQL Database

```bash
# Create RDS instance via AWS Console or CLI
aws rds create-db-instance \
    --db-instance-identifier vaani-postgres \
    --db-instance-class db.t3.micro \
    --engine postgres \
    --engine-version 15.4 \
    --master-username admin \
    --master-user-password YourStrongPassword123! \
    --allocated-storage 20 \
    --db-name vaani_db \
    --publicly-accessible \
    --backup-retention-period 7
```

Wait for RDS instance to be available:
```bash
aws rds wait db-instance-available --db-instance-identifier vaani-postgres
```

Get RDS endpoint:
```bash
aws rds describe-db-instances \
    --db-instance-identifier vaani-postgres \
    --query 'DBInstances[0].Endpoint.Address' \
    --output text
```

### Step 5: Setup Database Schema

```bash
# Connect to RDS and run schema
psql -h <rds-endpoint> -U admin -d vaani_db -f Database/schema.sql

# Update Azure keys
psql -h <rds-endpoint> -U admin -d vaani_db -c "UPDATE meetings SET azure_subscription_key = 'your_actual_key';"
```

### Step 6: Configure Environment Variables

Create `.ebextensions/environment.config`:

```bash
mkdir .ebextensions
cat > .ebextensions/environment.config << 'EOF'
option_settings:
  aws:elasticbeanstalk:application:environment:
    ASPNETCORE_ENVIRONMENT: Production
    ConnectionStrings__PostgreSQL: "Host=<your-rds-endpoint>;Port=5432;Database=vaani_db;Username=admin;Password=YourStrongPassword123!;SSL Mode=Require"
    Jwt__Secret: "ProductionSecretKeyAtLeast32CharactersLongForSecurity"
    Jwt__Issuer: "VaaniAPI"
    Jwt__Audience: "VaaniDesktopApp"
    Jwt__ExpirationHours: "4"
    Encryption__AesKey: "ProductionAES256Key32Characters!"
EOF
```

### Step 7: Create and Deploy Environment

```bash
# Create environment
eb create vaani-api-prod \
    --instance-type t3.small \
    --database.engine postgres \
    --database.version 15.4

# Deploy
eb deploy vaani-api-prod
```

### Step 8: Configure Security Groups

```bash
# Get security group ID
SG_ID=$(aws ec2 describe-security-groups \
    --filters "Name=group-name,Values=vaani-api-prod" \
    --query 'SecurityGroups[0].GroupId' \
    --output text)

# Allow HTTPS
aws ec2 authorize-security-group-ingress \
    --group-id $SG_ID \
    --protocol tcp \
    --port 443 \
    --cidr 0.0.0.0/0
```

### Step 9: Setup SSL Certificate

```bash
# Request certificate in AWS Certificate Manager
aws acm request-certificate \
    --domain-name api.vaani.com \
    --validation-method DNS

# Configure load balancer to use certificate (via AWS Console)
# EB Console ? Configuration ? Load balancer ? Add listener (443)
```

### Step 10: Verify Deployment

```bash
# Get application URL
eb status vaani-api-prod

# Test API
curl https://your-app-url.elasticbeanstalk.com/health
```

---

## ?? Option 2: AWS ECS with Fargate

### Step 1: Build and Push Docker Image

```bash
# Login to ECR
aws ecr get-login-password --region us-east-1 | \
    docker login --username AWS --password-stdin <account-id>.dkr.ecr.us-east-1.amazonaws.com

# Create ECR repository
aws ecr create-repository --repository-name vaani-api --region us-east-1

# Build image
docker build -t vaani-api .

# Tag image
docker tag vaani-api:latest <account-id>.dkr.ecr.us-east-1.amazonaws.com/vaani-api:latest

# Push image
docker push <account-id>.dkr.ecr.us-east-1.amazonaws.com/vaani-api:latest
```

### Step 2: Create ECS Cluster

```bash
aws ecs create-cluster --cluster-name vaani-cluster --region us-east-1
```

### Step 3: Create Task Definition

Create `ecs-task-definition.json`:

```json
{
  "family": "vaani-api-task",
  "networkMode": "awsvpc",
  "requiresCompatibilities": ["FARGATE"],
  "cpu": "512",
  "memory": "1024",
  "executionRoleArn": "arn:aws:iam::<account-id>:role/ecsTaskExecutionRole",
  "containerDefinitions": [
    {
      "name": "vaani-api",
      "image": "<account-id>.dkr.ecr.us-east-1.amazonaws.com/vaani-api:latest",
      "portMappings": [
        {
          "containerPort": 80,
          "protocol": "tcp"
        }
      ],
      "environment": [
        {
          "name": "ASPNETCORE_ENVIRONMENT",
          "value": "Production"
        },
        {
          "name": "ConnectionStrings__PostgreSQL",
          "value": "Host=<rds-endpoint>;Port=5432;Database=vaani_db;Username=admin;Password=<password>;SSL Mode=Require"
        }
      ],
      "logConfiguration": {
        "logDriver": "awslogs",
        "options": {
          "awslogs-group": "/ecs/vaani-api",
          "awslogs-region": "us-east-1",
          "awslogs-stream-prefix": "ecs"
        }
      }
    }
  ]
}
```

Register task:
```bash
aws ecs register-task-definition --cli-input-json file://ecs-task-definition.json
```

### Step 4: Create ECS Service with Load Balancer

```bash
# Create Application Load Balancer
aws elbv2 create-load-balancer \
    --name vaani-api-alb \
    --subnets subnet-xxx subnet-yyy \
    --security-groups sg-xxx

# Create target group
aws elbv2 create-target-group \
    --name vaani-api-tg \
    --protocol HTTP \
    --port 80 \
    --vpc-id vpc-xxx \
    --target-type ip \
    --health-check-path /health

# Create ECS service
aws ecs create-service \
    --cluster vaani-cluster \
    --service-name vaani-api-service \
    --task-definition vaani-api-task \
    --desired-count 2 \
    --launch-type FARGATE \
    --network-configuration "awsvpcConfiguration={subnets=[subnet-xxx,subnet-yyy],securityGroups=[sg-xxx],assignPublicIp=ENABLED}" \
    --load-balancers "targetGroupArn=arn:aws:elasticloadbalancing:us-east-1:xxx:targetgroup/vaani-api-tg/xxx,containerName=vaani-api,containerPort=80"
```

---

## ??? Option 3: AWS EC2 Deployment

### Step 1: Launch EC2 Instance

```bash
aws ec2 run-instances \
    --image-id ami-0c55b159cbfafe1f0 \
    --instance-type t3.small \
    --key-name your-key-pair \
    --security-group-ids sg-xxx \
    --subnet-id subnet-xxx \
    --tag-specifications 'ResourceType=instance,Tags=[{Key=Name,Value=vaani-api}]'
```

### Step 2: Connect and Setup

```bash
ssh -i your-key.pem ec2-user@<instance-ip>

# Install .NET 8
sudo rpm -Uvh https://packages.microsoft.com/config/centos/7/packages-microsoft-prod.rpm
sudo yum install dotnet-sdk-8.0 -y

# Install Nginx
sudo yum install nginx -y
```

### Step 3: Deploy Application

```bash
# Transfer files
scp -i your-key.pem -r publish/* ec2-user@<instance-ip>:/home/ec2-user/vaani-api/

# Setup systemd service
sudo nano /etc/systemd/system/vaani-api.service
```

Add:
```ini
[Unit]
Description=Vaani API

[Service]
WorkingDirectory=/home/ec2-user/vaani-api
ExecStart=/usr/bin/dotnet /home/ec2-user/vaani-api/Vaani.API.dll
Restart=always
RestartSec=10
SyslogIdentifier=vaani-api
User=ec2-user
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
```

Start service:
```bash
sudo systemctl enable vaani-api
sudo systemctl start vaani-api
```

### Step 4: Configure Nginx

```bash
sudo nano /etc/nginx/nginx.conf
```

Add:
```nginx
server {
    listen 80;
    server_name api.vaani.com;

    location / {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection keep-alive;
        proxy_set_header Host $host;
        proxy_cache_bypass $http_upgrade;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
    }
}
```

Restart Nginx:
```bash
sudo systemctl restart nginx
```

---

## ?? Security Best Practices

### 1. Use AWS Secrets Manager

```bash
# Store secrets
aws secretsmanager create-secret \
    --name vaani/jwt-secret \
    --secret-string "YourSuperSecretKey"

aws secretsmanager create-secret \
    --name vaani/encryption-key \
    --secret-string "YourAES256Key"

aws secretsmanager create-secret \
    --name vaani/postgres-password \
    --secret-string "YourDatabasePassword"
```

Update code to retrieve secrets:
```csharp
// Add NuGet: AWSSDK.SecretsManager
var jwtSecret = await GetSecretAsync("vaani/jwt-secret");
```

### 2. Enable WAF (Web Application Firewall)

```bash
aws wafv2 create-web-acl \
    --name vaani-waf \
    --scope REGIONAL \
    --default-action Block={} \
    --rules file://waf-rules.json
```

### 3. Setup CloudWatch Alarms

```bash
aws cloudwatch put-metric-alarm \
    --alarm-name vaani-high-cpu \
    --alarm-description "Alert when CPU exceeds 80%" \
    --metric-name CPUUtilization \
    --namespace AWS/ECS \
    --statistic Average \
    --period 300 \
    --threshold 80 \
    --comparison-operator GreaterThanThreshold
```

---

## ?? Monitoring & Logging

### CloudWatch Logs

```bash
# Create log group
aws logs create-log-group --log-group-name /aws/elasticbeanstalk/vaani-api/app

# View logs
aws logs tail /aws/elasticbeanstalk/vaani-api/app --follow
```

### Application Insights

Add to `Program.cs`:
```csharp
builder.Services.AddApplicationInsightsTelemetry();
```

---

## ?? CI/CD Pipeline

### GitHub Actions Example

Create `.github/workflows/deploy.yml`:

```yaml
name: Deploy to AWS

on:
  push:
    branches: [main]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v2
      
      - name: Setup .NET
        uses: actions/setup-dotnet@v1
        with:
          dotnet-version: 8.0.x
      
      - name: Publish
        run: dotnet publish -c Release -o publish
      
      - name: Deploy to EB
        uses: einaregilsson/beanstalk-deploy@v21
        with:
          aws_access_key: ${{ secrets.AWS_ACCESS_KEY_ID }}
          aws_secret_key: ${{ secrets.AWS_SECRET_ACCESS_KEY }}
          application_name: vaani-api
          environment_name: vaani-api-prod
          version_label: ${{ github.sha }}
          region: us-east-1
          deployment_package: publish
```

---

## ?? Cost Estimation

### Elastic Beanstalk (Small)
- EC2 t3.small: $15/month
- RDS db.t3.micro: $15/month
- Load Balancer: $16/month
- **Total: ~$46/month**

### ECS Fargate (2 tasks)
- Fargate: $30/month
- RDS db.t3.micro: $15/month
- Load Balancer: $16/month
- **Total: ~$61/month**

---

## ?? Troubleshooting

### Connection Issues

```bash
# Test database connection
psql -h <rds-endpoint> -U admin -d vaani_db

# Check security groups
aws ec2 describe-security-groups --group-ids sg-xxx
```

### Application Logs

```bash
# EB logs
eb logs

# ECS logs
aws logs tail /ecs/vaani-api --follow

# EC2 logs
ssh -i key.pem ec2-user@instance-ip
sudo journalctl -u vaani-api -f
```

---

## ? Post-Deployment Checklist

- [ ] Database schema deployed
- [ ] Azure keys updated in database
- [ ] Environment variables configured
- [ ] SSL certificate installed
- [ ] Health check endpoint responding
- [ ] JWT authentication working
- [ ] CloudWatch alarms configured
- [ ] Backup strategy in place
- [ ] Desktop app updated with API URL

---

## ?? Support

For deployment issues:
- GitHub Issues: https://github.com/sanjaykrpandit/vaani/issues
- AWS Support: https://console.aws.amazon.com/support/
