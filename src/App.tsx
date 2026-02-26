/**
 * @license
 * SPDX-License-Identifier: Apache-2.0
 */

import { useState, useEffect, useMemo } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { Trophy, Timer, ChevronRight, RotateCcw, CheckCircle2, XCircle, Star, Target, Shield, Users, UserPlus, Trash2, Play, Hash, LogIn } from 'lucide-react';
import { io, Socket } from 'socket.io-client';
import { GameState, Player, Question } from './types';

const QUESTIONS_PER_LEVEL = 5;
const TOTAL_LEVELS = 10;

export default function App() {
  const [socket, setSocket] = useState<Socket | null>(null);
  const [roomId, setRoomId] = useState('');
  const [playerName, setPlayerName] = useState('');
  const [gameState, setGameState] = useState<GameState | null>(null);
  const [localChoice, setLocalChoice] = useState<string | null>(null);
  const [isJoined, setIsJoined] = useState(false);

  useEffect(() => {
    const newSocket = io();
    setSocket(newSocket);

    newSocket.on("room_update", (updatedRoom: GameState) => {
      setGameState(updatedRoom);
      // Reset local choice when moving to a new question
      if (updatedRoom.status === 'PLAYING' && Object.keys(updatedRoom.playerChoices).length === 0) {
        setLocalChoice(null);
      }
    });

    return () => {
      newSocket.disconnect();
    };
  }, []);

  const joinRoom = () => {
    if (roomId.trim() && playerName.trim() && socket) {
      socket.emit("join_room", { roomId: roomId.trim().toUpperCase(), playerName: playerName.trim() });
      setIsJoined(true);
    }
  };

  const submitChoice = (choice: string) => {
    if (gameState?.status === 'PLAYING' && !localChoice && socket) {
      setLocalChoice(choice);
      socket.emit("submit_choice", { roomId: gameState.id, choice });
    }
  };

  const nextStep = () => {
    if (socket && gameState) {
      socket.emit("next_step", { roomId: gameState.id });
    }
  };

  const nextLevel = () => {
    if (socket && gameState) {
      socket.emit("next_level", { roomId: gameState.id });
    }
  };

  const currentQuestion = useMemo(() => {
    if (!gameState) return null;
    return gameState.questions[(gameState.currentLevel - 1) * QUESTIONS_PER_LEVEL + gameState.currentQuestionIndex];
  }, [gameState]);

  const getWinner = () => {
    if (!gameState) return null;
    return [...gameState.players].sort((a, b) => b.score - a.score)[0];
  };

  if (!isJoined) {
    return (
      <div className="min-h-screen bg-[#0a0f1e] text-white font-sans flex items-center justify-center p-4">
        <motion.div
          initial={{ opacity: 0, y: 20 }}
          animate={{ opacity: 1, y: 0 }}
          className="max-w-md w-full space-y-8 bg-slate-800/40 p-8 rounded-[2.5rem] border border-slate-700/50 backdrop-blur-xl shadow-2xl"
        >
          <div className="text-center space-y-4">
            <div className="bg-emerald-500/20 w-20 h-20 rounded-3xl flex items-center justify-center mx-auto mb-6 rotate-3">
              <Trophy className="w-10 h-10 text-emerald-500" />
            </div>
            <h1 className="text-4xl font-black italic uppercase tracking-tighter">
              Futbol <span className="text-emerald-500">Quiz</span> Master
            </h1>
            <p className="text-slate-400">Yarışmaya katılmak için bir oda ID'si ve adınızı girin.</p>
          </div>

          <div className="space-y-4">
            <div className="space-y-2">
              <label className="text-xs font-black uppercase tracking-widest text-slate-500 ml-2">Oda ID</label>
              <div className="relative">
                <Hash className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-slate-500" />
                <input
                  type="text"
                  value={roomId}
                  onChange={(e) => setRoomId(e.target.value)}
                  placeholder="Örn: FINAL2024"
                  className="w-full bg-slate-900/50 border border-slate-700 rounded-2xl pl-12 pr-4 py-4 focus:outline-none focus:border-emerald-500 transition-all font-bold uppercase"
                />
              </div>
            </div>

            <div className="space-y-2">
              <label className="text-xs font-black uppercase tracking-widest text-slate-500 ml-2">Adınız</label>
              <div className="relative">
                <Users className="absolute left-4 top-1/2 -translate-y-1/2 w-5 h-5 text-slate-500" />
                <input
                  type="text"
                  value={playerName}
                  onChange={(e) => setPlayerName(e.target.value)}
                  placeholder="Adınızı girin..."
                  className="w-full bg-slate-900/50 border border-slate-700 rounded-2xl pl-12 pr-4 py-4 focus:outline-none focus:border-emerald-500 transition-all font-bold"
                />
              </div>
            </div>

            <button
              onClick={joinRoom}
              disabled={!roomId.trim() || !playerName.trim()}
              className="w-full py-4 bg-emerald-600 hover:bg-emerald-500 disabled:opacity-50 text-white font-black text-xl rounded-2xl transition-all flex items-center justify-center gap-3 shadow-lg mt-4 group"
            >
              <LogIn className="w-6 h-6 group-hover:translate-x-1 transition-transform" />
              YARIŞMAYA KATIL
            </button>
          </div>
        </motion.div>
      </div>
    );
  }

  if (!gameState || !currentQuestion) {
    return (
      <div className="min-h-screen bg-[#0a0f1e] text-white font-sans flex items-center justify-center">
        <div className="text-center space-y-4">
          <div className="w-12 h-12 border-4 border-emerald-500 border-t-transparent rounded-full animate-spin mx-auto" />
          <p className="text-slate-400 font-bold animate-pulse">Odaya bağlanılıyor...</p>
        </div>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-[#0a0f1e] text-white font-sans selection:bg-emerald-500/30">
      <div className="max-w-4xl mx-auto px-4 py-8 md:py-12">
        
        <AnimatePresence mode="wait">
          {gameState.status === 'PLAYING' && (
            <motion.div
              key="playing"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="space-y-6"
            >
              {/* Player Status Bar */}
              <div className="flex flex-wrap items-center justify-center gap-3 mb-4">
                {gameState.players.map((p) => {
                  const hasAnswered = gameState.playerChoices[p.id] !== undefined;
                  return (
                    <div
                      key={p.id}
                      className={`px-4 py-2 rounded-xl border-2 transition-all flex items-center gap-2 ${
                        p.id === socket?.id
                          ? 'bg-emerald-500/10 border-emerald-500 shadow-[0_0_15px_rgba(16,185,129,0.2)]'
                          : 'bg-slate-900 border-slate-800'
                      }`}
                    >
                      <div className={`w-2 h-2 rounded-full ${hasAnswered ? 'bg-emerald-500 animate-pulse' : 'bg-slate-700'}`} />
                      <span className="font-black text-xs uppercase">{p.name}</span>
                      <span className="text-[10px] bg-slate-800 px-1.5 py-0.5 rounded text-slate-400">{p.score}</span>
                    </div>
                  );
                })}
              </div>

              <div className="flex justify-between items-center bg-slate-800/40 p-4 rounded-2xl border border-slate-700/50">
                <div className="flex items-center gap-3">
                  <div className="bg-emerald-500/20 p-2 rounded-lg">
                    <Hash className="w-5 h-5 text-emerald-500" />
                  </div>
                  <div>
                    <p className="text-[10px] uppercase tracking-widest text-slate-400 font-bold">Oda ID</p>
                    <p className="text-xl font-black text-emerald-400">{gameState.id}</p>
                  </div>
                </div>

                <div className="text-center">
                  <p className="text-[10px] uppercase tracking-widest text-slate-400 font-bold">Seviye {gameState.currentLevel}</p>
                  <p className="text-lg font-black">Soru {gameState.currentQuestionIndex + 1}/5</p>
                </div>

                <div className="flex items-center gap-3">
                  <div className="bg-blue-500/20 p-2 rounded-lg">
                    <Users className="w-5 h-5 text-blue-500" />
                  </div>
                  <div>
                    <p className="text-[10px] uppercase tracking-widest text-slate-400 font-bold">Oyuncu</p>
                    <p className="text-xl font-black text-blue-400">{gameState.players.length}</p>
                  </div>
                </div>
              </div>

              <div className="bg-slate-800/30 p-8 rounded-[2rem] border border-slate-700/50 shadow-2xl relative overflow-hidden">
                <div className="absolute top-0 left-0 w-1 h-full bg-emerald-500" />
                <p className="text-emerald-500 font-mono text-sm mb-4 uppercase tracking-tighter font-bold">
                  {currentQuestion.category}
                </p>
                <h2 className="text-2xl md:text-3xl font-bold leading-tight mb-8">
                  {currentQuestion.text}
                </h2>

                <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                  {currentQuestion.options.map((option) => {
                    const isSelected = localChoice === option;
                    return (
                      <button
                        key={option}
                        disabled={!!localChoice}
                        onClick={() => submitChoice(option)}
                        className={`w-full p-5 text-left rounded-2xl border-2 transition-all font-bold text-lg flex justify-between items-center ${
                          isSelected
                            ? 'bg-emerald-500/20 border-emerald-500 text-emerald-400'
                            : !!localChoice
                            ? 'bg-slate-800/30 border-slate-800 opacity-50'
                            : 'bg-slate-800/50 border-slate-700 hover:border-emerald-500 hover:bg-slate-700/50'
                        }`}
                      >
                        {option}
                        {isSelected && <CheckCircle2 className="w-6 h-6" />}
                      </button>
                    );
                  })}
                </div>
              </div>

              {localChoice && (
                <div className="text-center animate-bounce">
                  <p className="text-emerald-500 font-bold">Diğer oyuncular bekleniyor...</p>
                </div>
              )}
            </motion.div>
          )}

          {gameState.status === 'REVEAL' && (
            <motion.div
              key="reveal"
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              className="space-y-6"
            >
              <div className="bg-slate-800/30 p-8 rounded-[2rem] border border-slate-700/50 shadow-2xl">
                <h2 className="text-2xl font-bold mb-6 text-center">Soru Sonucu</h2>
                
                <div className="space-y-4 mb-8">
                  {gameState.players.map((player) => {
                    const choice = gameState.playerChoices[player.id];
                    const isCorrect = choice === currentQuestion.correctAnswer;
                    return (
                      <div key={player.id} className={`flex items-center justify-between p-4 rounded-2xl border ${player.id === socket?.id ? 'bg-emerald-500/5 border-emerald-500/30' : 'bg-slate-900/50 border-slate-800'}`}>
                        <div className="flex flex-col">
                          <span className="text-[10px] text-slate-500 uppercase font-black">{player.name} {player.id === socket?.id && '(Siz)'}</span>
                          <span className={`font-bold ${choice ? 'text-white' : 'text-red-500 italic'}`}>
                            {choice || 'Cevap Vermedi'}
                          </span>
                        </div>
                        <div className="flex items-center gap-3">
                          {isCorrect ? (
                            <div className="flex items-center gap-2 text-emerald-500">
                              <CheckCircle2 className="w-6 h-6" />
                              <span className="font-black text-sm">+50</span>
                            </div>
                          ) : (
                            <XCircle className="w-6 h-6 text-red-500" />
                          )}
                        </div>
                      </div>
                    );
                  })}
                </div>

                <div className="bg-emerald-500/10 border border-emerald-500/30 p-6 rounded-3xl text-center relative overflow-hidden">
                  <div className="absolute top-0 right-0 p-2 opacity-10">
                    <Target className="w-12 h-12" />
                  </div>
                  <p className="text-xs text-emerald-500 uppercase font-black mb-1 tracking-widest">Doğru Cevap</p>
                  <p className="text-3xl font-black text-emerald-400">{currentQuestion.correctAnswer}</p>
                </div>
              </div>

              <button
                onClick={nextStep}
                className="w-full py-4 bg-blue-600 hover:bg-blue-500 text-white font-black text-xl rounded-2xl transition-all flex items-center justify-center gap-2 shadow-lg"
              >
                DEVAM ET
                <ChevronRight className="w-6 h-6" />
              </button>
            </motion.div>
          )}

          {gameState.status === 'LEVEL_COMPLETE' && (
            <motion.div
              key="level-complete"
              initial={{ opacity: 0, scale: 0.9 }}
              animate={{ opacity: 1, scale: 1 }}
              className="text-center space-y-8 py-12"
            >
              <div className="bg-emerald-500 w-24 h-24 rounded-full flex items-center justify-center mx-auto shadow-[0_0_40px_rgba(16,185,129,0.4)]">
                <Trophy className="w-12 h-12 text-white" />
              </div>
              <div className="space-y-2">
                <h2 className="text-4xl font-black italic uppercase">TEBRİKLER!</h2>
                <p className="text-slate-400 text-xl">Seviye {gameState.currentLevel} Tamamlandı.</p>
              </div>
              
              <div className="grid grid-cols-1 gap-3 max-w-sm mx-auto">
                {[...gameState.players].sort((a,b) => b.score - a.score).map((p, i) => (
                  <div key={p.id} className={`flex justify-between items-center p-4 rounded-2xl border ${i === 0 ? 'bg-emerald-500/10 border-emerald-500/50' : 'bg-slate-800/50 border-slate-700'}`}>
                    <div className="flex items-center gap-3">
                      <span className="text-slate-500 font-bold">#{i+1}</span>
                      <span className="font-bold">{p.name}</span>
                    </div>
                    <span className="font-black text-emerald-400">{p.score}</span>
                  </div>
                ))}
              </div>

              <button
                onClick={nextLevel}
                className="px-12 py-4 bg-emerald-600 hover:bg-emerald-500 text-white font-black text-xl rounded-full transition-all shadow-lg"
              >
                SONRAKİ SEVİYE
                <ChevronRight className="inline-block ml-2" />
              </button>
            </motion.div>
          )}

          {gameState.status === 'GAME_OVER' && (
            <motion.div
              key="game-over"
              initial={{ opacity: 0, y: 20 }}
              animate={{ opacity: 1, y: 0 }}
              className="text-center space-y-8 py-8"
            >
              <div className="relative inline-block">
                <Trophy className="w-32 h-32 mx-auto text-yellow-500" />
                <div className="absolute -bottom-4 -right-4 bg-emerald-500 p-4 rounded-full border-4 border-[#0a0f1e]">
                  <Star className="w-8 h-8 text-white fill-white" />
                </div>
              </div>

              <div className="space-y-4">
                <h2 className="text-5xl font-black italic uppercase tracking-tighter">OYUN BİTTİ!</h2>
                <div className="bg-emerald-500/20 border border-emerald-500/50 p-6 rounded-[2rem] max-w-md mx-auto">
                  <p className="text-slate-400 uppercase font-bold text-sm mb-2">ŞAMPİYON</p>
                  <p className="text-4xl font-black text-white">{getWinner()?.name}</p>
                  <p className="text-emerald-400 font-bold mt-2">{getWinner()?.score} PUAN</p>
                </div>
              </div>

              <div className="bg-slate-800/30 p-8 rounded-[2rem] border border-slate-700/50 max-w-lg mx-auto">
                <h3 className="text-xl font-black mb-6 uppercase italic">Final Skorbord</h3>
                <div className="space-y-3">
                  {[...gameState.players].sort((a, b) => b.score - a.score).map((p, i) => (
                    <div key={p.id} className={`flex items-center justify-between p-4 rounded-2xl border ${i === 0 ? 'bg-emerald-500/10 border-emerald-500/50' : 'bg-slate-900/50 border-slate-800'}`}>
                      <div className="flex items-center gap-4">
                        <span className="w-6 text-slate-500 font-bold">#{i + 1}</span>
                        <span className="font-bold">{p.name}</span>
                      </div>
                      <span className="font-black text-xl">{p.score}</span>
                    </div>
                  ))}
                </div>
              </div>

              <button
                onClick={() => window.location.reload()}
                className="px-12 py-4 bg-emerald-600 hover:bg-emerald-500 text-white font-black text-xl rounded-full transition-all flex items-center gap-2 mx-auto"
              >
                <RotateCcw className="w-6 h-6" />
                YENİ YARIŞMA
              </button>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
