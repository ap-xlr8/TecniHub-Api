$ErrorActionPreference = 'Stop'

Write-Output 'Cleaning demo operational tables in tecnihub_dev (preserving SEPOMEX postal_codes, roles and users)...'

@'
TRUNCATE TABLE
  service_reviews,
  chat_messages,
  chat_conversations,
  repair_request_images,
  repair_requests,
  spare_part_images,
  spare_parts,
  user_notifications,
  push_subscriptions
CASCADE;

SELECT 'postal_codes' AS table_name, COUNT(*) AS rows FROM postal_codes
UNION ALL
SELECT 'users', COUNT(*) FROM users;
'@ | docker exec -i tecnihub-postgres-dev psql -U tecnihub -d tecnihub_dev

Write-Output 'Demo operational tables reset cleanly.'
