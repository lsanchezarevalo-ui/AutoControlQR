# Scaffold README

This branch contains a minimal scaffold for the AutoControl QR MVP.

Backend: Node + Express + Prisma
Frontend: React + Vite (PWA-ready)

Quick start (with docker-compose):

1. Copy .env from backend/.env.example and set DATABASE_URL
2. Start services: docker-compose up --build
3. From backend, run migrations:
   npx prisma migrate deploy

Frontend:
1. cd frontend
2. npm install
3. npm run dev

