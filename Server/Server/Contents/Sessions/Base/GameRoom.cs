﻿using Google.Protobuf;
using Google.Protobuf.Protocol;
using Server.Contents.Objects.Player;
using Server.Session;
using Server.Utils;
using ServerCore;
using System.Numerics;

namespace Server.Contents.Sessions.Base {
    //public struct GameRoomInfo {
    //    public int RoomID;
    //    public int MaxCapacity;
    //    public int ConnectedCount;
    //}
    public class GameRoom : IJobQueue {
        protected const int max_capacity = 5;
        protected JobQueue _jobQueue = new JobQueue();

        protected volatile Dictionary<int, ClientSession> _sessions = new Dictionary<int, ClientSession>();

        protected Dictionary<int, Player> _players = new Dictionary<int, Player>();

        protected List<System.Timers.Timer> _timerList = new List<System.Timers.Timer>();

        public int RoomCode { get; set; }
        public bool CanAccept { get { return max_capacity - _sessions.Count > 0; } }

        #region GameRoom Management Functions

        public virtual void Init() {
            AddJobTimer(Update, 250f);
            AddJobTimer(SyncPlayerTransform, 500f);
        }

        protected void AddJobTimer(Action action, float times) {
            if(action == null)
                return;

            System.Timers.Timer timer = new System.Timers.Timer();
            timer.Interval = times;
            timer.Elapsed += (s, e) => action.Invoke();
            timer.AutoReset = true;
            timer.Enabled = true;

            _timerList.Add(timer);
        }

        public virtual void Broadcast(IMessage packet) {
            if(_sessions.Count == 0)
                return;

            foreach(ClientSession session in _sessions.Values) {
                session.Send(packet);
            }
        }

        public virtual void DestroyRoom() {
            for(int i = 0; i < _timerList.Count; i++) {
                _timerList[i].Stop();
            }
            _timerList.Clear();
            _sessions = null;
            _jobQueue.Clear();
            _jobQueue = null;

            Console.WriteLine($"Try to Destory GameRoom {RoomCode}");
            GameRoomManager.Instance.DestroyRoom(this);
        }

        public void Push(Action action) {
            if(_jobQueue == null)
                return;

            _jobQueue.Push(action);
        }

        public virtual void Enter(ClientSession session, pAreaType prevArea, pAreaType destArea) {
            if(session == null)
                return;
            if(_sessions.ContainsKey(session.AuthCode))
                return;

            _sessions.Add(session.AuthCode, session);
            session.Section = this;

            Console.WriteLine($"Client{session.AuthCode} Entered Room{RoomCode}");

            //TODO: 오브젝트 데이터 패킷으로 신규 유저에게 전송
            {
                S_Load_Players playerList = GetpUserInGameDatas(session.AuthCode);
                session.Send(playerList);
                S_Load_Items itemList = new S_Load_Items();
                session.Send(itemList);
                S_Load_Fields fieldList = new S_Load_Fields();
                session.Send(fieldList);
            }

            S_Spawn spawn = new S_Spawn();
            spawn.AuthCode = session.AuthCode;
            spawn.PrevArea = prevArea;
            spawn.DestArea = destArea;

            Push(() => Broadcast(spawn));
        }

        public virtual void Leave(int authCode) {
            Console.WriteLine($"Player {authCode} Try to Leave");
            bool result = _sessions.Remove(authCode);
            if(result == false) {
                return;
            }

            //TODO: Broadcast Someone's Leave

            S_Player_Leave playerLeave = new S_Player_Leave();
            playerLeave.AuthCode = authCode;

            Push(() => Broadcast(playerLeave));

            if(_sessions.Count == 0)
                Push(() => DestroyRoom());
        }

        public virtual void Update() {
            foreach(int key in _players.Keys) {
                if(_sessions.ContainsKey(key))
                    _players[key].Update(_sessions[key].TimeDelay);
            }

            //TODO: 다른 Update가 필요한 오브젝트 컨테이너를 여기서 호출
        }
        #endregion


        #region User Management Functions
        public void Spawn_Player(int authCode, pVector3 position) {
            if(_players.ContainsKey(authCode))
                return;

            Player player = new Player(authCode, position.ToVector3());
            _players.Add(authCode, player);
            player.isSpawned = true;
        }

        public void Sync_PlayerPosition(int authCode, pVector3 position, bool checkFlag = true) {
            Player player = null;

            if(position == null)
                return;

            if(_players.TryGetValue(authCode, out player) == false)
                return;

            double delayFloat = _sessions[authCode].TimeDelay;

            Console.WriteLine($"DistanceSquared: {Vector3.Distance(position.ToVector3(), player.position)}");
            //TODO: 여기서도 해당 유저와의 RTT / 2로 Environment.TickCount64를 대체 해야함
            if(checkFlag == true && Vector3.Distance(position.ToVector3(), player.position) > ( player.speed * delayFloat ) + 20.0f) {
                //Push(() => Leave(player.AuthCode));
                //Console.WriteLine($"Player{player.AuthCode} Disconnected Due to Irregular Moving Distance {Vector3.DistanceSquared(position.ToVector3(), player.position)}");
                //Console.WriteLine($"Player's Position: {player.position}, Target Position: {position}");
                //Console.WriteLine($"Player Speed: {player.speed} TimeDelay: { delayFloat }");
                //Console.WriteLine($"Benchmarking Value: {( player.speed * delayFloat ) + 20.0f}");
                //return;
            }

            player.position = position.ToVector3();
        }

        public void Sync_PlayerRotation(int authCode, pQuaternion rotation) {
            Player player = null;

            if(_players.TryGetValue(authCode, out player)) {
                player.rotation = new Quaternion(rotation.X, rotation.Y, rotation.Z, rotation.W);

                S_Broadcast_Look_Rotation look = new S_Broadcast_Look_Rotation();

                look.AuthCode = authCode;
                look.Rotation = rotation;

                Push(() => Broadcast(look));
            }
        }

        /// <summary>
        /// Get All Users' Data in this Session. Include AI.
        /// </summary>
        /// <param name="excAuthCode">AuthCode for Exception.</param>
        /// <returns></returns>
        public S_Load_Players GetpUserInGameDatas(int excAuthCode = -1) {
            S_Load_Players list = new S_Load_Players();

            foreach(Player player in _players.Values) {
                if(player.AuthCode == excAuthCode)
                    continue;

                pObjectData data = new pObjectData();
                data.AuthCode = player.AuthCode;

                data.Position = player.position.TopVector3();
                data.Rotation = player.rotation.TopQuaternion();
                list.ObjectList.Add(data);
            }

            return list;
        }

        public void HandleMove(ClientSession session, C_Move move) {
            Player player = null;

            if(_players.TryGetValue(session.AuthCode, out player) == false)
                return;

            player.moveDir = new Vector3(move.Dir.X, move.Dir.Y, move.Dir.Z);
            player.stance = move.Stance;

            S_Move_Broadcast moveBroad = new S_Move_Broadcast();

            moveBroad.AuthCode = session.AuthCode;
            moveBroad.Dir = move.Dir;
            moveBroad.Stance = move.Stance;

            Push(() => Broadcast(moveBroad));
        }

        public void SyncPlayerTransform() {
            S_Sync_Player_Transform sync = new S_Sync_Player_Transform();
            pObjectData data = new pObjectData();

            foreach(Player player in _players.Values) {
                data = new pObjectData();
                data.AuthCode = player.AuthCode;
                data.Position = player.position.TopVector3();
                data.Rotation = player.rotation.TopQuaternion();

                sync.PlayerTransforms.Add(data);
            }

            Push(() => Broadcast(sync));
        }
        #endregion
    }
}