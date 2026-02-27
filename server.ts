import express from "express";
import { createServer } from "http";
import { Server } from "socket.io";
import { createServer as createViteServer } from "vite";
import { QUESTIONS } from "./src/questions";
import type { Question } from "./src/types";

const PORT = 3000;
const QUESTIONS_PER_LEVEL = 5;

interface RoomState {
  id: string;
  players: { id: string; name: string; score: number; answers: any[] }[];
  currentLevel: number;
  currentQuestionIndex: number;
  status: 'PLAYING' | 'REVEAL' | 'LEVEL_COMPLETE' | 'GAME_OVER';
  questions: Question[];
  playerChoices: Record<string, string>; // socketId -> choice
}

const rooms: Record<string, RoomState> = {};

// Shuffle questions helper
const shuffleArray = <T,>(array: T[]): T[] => {
  const shuffled = [...array];
  for (let i = shuffled.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [shuffled[i], shuffled[j]] = [shuffled[j], shuffled[i]];
  }
  return shuffled;
};

async function startServer() {
  const app = express();
  const httpServer = createServer(app);
  const io = new Server(httpServer);

  if (process.env.NODE_ENV !== "production") {
    const vite = await createViteServer({
      server: { middlewareMode: true },
      appType: "spa",
    });
    app.use(vite.middlewares);
  } else {
    app.use(express.static("dist"));
  }

  io.on("connection", (socket) => {
    console.log("User connected:", socket.id);

    socket.on("join_room", ({ roomId, playerName }) => {
      socket.join(roomId);
      
      if (!rooms[roomId]) {
        // Pick 50 random questions from the pool
        const pool = [...QUESTIONS];
        const selectedQuestions = [];
        for (let i = 0; i < 50 && pool.length > 0; i++) {
          const randomIndex = Math.floor(Math.random() * pool.length);
          selectedQuestions.push(pool.splice(randomIndex, 1)[0]);
        }

        rooms[roomId] = {
          id: roomId,
          players: [],
          currentLevel: 1,
          currentQuestionIndex: 0,
          status: 'PLAYING',
          questions: selectedQuestions,
          playerChoices: {},
        };
      }

      const room = rooms[roomId];
      
      // Check if player already in room
      const existingPlayer = room.players.find(p => p.id === socket.id);
      if (!existingPlayer) {
        room.players.push({
          id: socket.id,
          name: playerName,
          score: 0,
          answers: [],
        });
      }

      io.to(roomId).emit("room_update", room);
    });

    socket.on("submit_choice", ({ roomId, choice }) => {
      const room = rooms[roomId];
      if (!room) return;

      room.playerChoices[socket.id] = choice;

      // Check if all players in the room have answered
      const allAnswered = room.players.every(p => room.playerChoices[p.id] !== undefined);

      if (allAnswered) {
        room.status = 'REVEAL';
        
        // Update scores
        const currentQuestion = room.questions[(room.currentLevel - 1) * QUESTIONS_PER_LEVEL + room.currentQuestionIndex];
        
        room.players.forEach(player => {
          const playerChoice = room.playerChoices[player.id];
          const isCorrect = playerChoice === currentQuestion.correctAnswer;
          if (isCorrect) {
            player.score += 50; // Simple scoring for now
          }
          player.answers.push({ questionId: currentQuestion.id, isCorrect });
        });

        io.to(roomId).emit("room_update", room);
      } else {
        // Just notify others that someone answered
        io.to(roomId).emit("player_answered", { socketId: socket.id });
      }
    });

    socket.on("next_step", ({ roomId }) => {
      const room = rooms[roomId];
      if (!room) return;

      if (room.currentQuestionIndex < QUESTIONS_PER_LEVEL - 1) {
        room.currentQuestionIndex++;
        room.status = 'PLAYING';
        room.playerChoices = {};
      } else if (room.currentLevel < 10) {
        room.status = 'LEVEL_COMPLETE';
      } else {
        room.status = 'GAME_OVER';
      }

      io.to(roomId).emit("room_update", room);
    });

    socket.on("next_level", ({ roomId }) => {
      const room = rooms[roomId];
      if (!room) return;

      room.currentLevel++;
      room.currentQuestionIndex = 0;
      room.status = 'PLAYING';
      room.playerChoices = {};

      io.to(roomId).emit("room_update", room);
    });

    socket.on("disconnect", () => {
      console.log("User disconnected:", socket.id);
      // Optional: Remove player from room or mark as inactive
      for (const roomId in rooms) {
        const room = rooms[roomId];
        const playerIndex = room.players.findIndex(p => p.id === socket.id);
        if (playerIndex !== -1) {
          // We keep the player in the list but could notify others
          io.to(roomId).emit("player_disconnected", { socketId: socket.id });
        }
      }
    });
  });

  httpServer.listen(PORT, "0.0.0.0", () => {
    console.log(`Server running on http://localhost:${PORT}`);
  });
}

startServer();
