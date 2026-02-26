export interface Question {
  id: string;
  text: string;
  options: string[];
  correctAnswer: string;
  category: string;
}

export interface Player {
  id: string;
  name: string;
  score: number;
  answers: { questionId: string; isCorrect: boolean }[];
}

export interface GameState {
  id: string;
  players: Player[];
  currentLevel: number;
  currentQuestionIndex: number;
  status: 'PLAYING' | 'REVEAL' | 'LEVEL_COMPLETE' | 'GAME_OVER';
  questions: Question[];
  playerChoices: Record<string, string>;
}
