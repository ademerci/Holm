import 'package:material_ui/material_ui.dart';
import 'package:flutter/services.dart';
import 'package:share_plus/share_plus.dart';

import '../app_state.dart';
import '../net/friends.dart';
import 'theme.dart';
import 'widgets.dart';

/// Holm's quick reactions, in its order (ChatReaction.Defaults).
const quickReactions = ['❤️', '😂', '😮', '😢', '😡', '👍'];

class ChatListPage extends StatelessWidget {
  final AppState state;
  const ChatListPage(this.state, {super.key});

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: state,
        builder: (context, _) {
          final friends = [...state.friends]..sort((a, b) {
              final la = state.chatWith(a).lastOrNull?.sent ?? 0, lb = state.chatWith(b).lastOrNull?.sent ?? 0;
              return lb.compareTo(la);
            });
          return ListView(
            padding: const EdgeInsets.only(bottom: 24),
            children: [
              PageHeader('Chat', 'Messages with your friends, sealed so only you two can read them',
                  trailing: RoundIconButton(Icons.person_add_alt_rounded,
                      tooltip: 'Add friend', onPressed: () => showAddFriend(context, state))),
              if (state.inboxError != null)
                HolmCard(
                  color: HolmColors.cardStrong,
                  child: Row(children: [
                    const Icon(Icons.cloud_off_rounded, color: HolmColors.gold),
                    const SizedBox(width: 12),
                    Expanded(child: Text('${state.inboxError}. Trying again…', style: const TextStyle(color: HolmColors.dim))),
                  ]),
                ),
              if (state.incoming.isNotEmpty) ...[
                const SectionLabel('Friend requests'),
                for (final r in state.incoming)
                  HolmCard(
                    child: Row(children: [
                      Avatar(r.card.name, size: 44),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                          Text(r.card.name, style: const TextStyle(fontWeight: FontWeight.w600, fontSize: 16)),
                          const Text('Would like to be friends', style: TextStyle(color: HolmColors.dim, fontSize: 13)),
                        ]),
                      ),
                      RoundIconButton(Icons.close_rounded, tooltip: 'Decline', onPressed: () => state.answerRequest(r, false)),
                      RoundIconButton(Icons.check_rounded,
                          tooltip: 'Accept', color: HolmColors.green, filled: true, onPressed: () => state.answerRequest(r, true)),
                    ]),
                  ),
              ],
              if (state.outgoing.isNotEmpty) ...[
                const SectionLabel('Waiting for an answer'),
                for (final r in state.outgoing)
                  HolmCard(
                    child: Row(children: [
                      Avatar(r.card.name, size: 40),
                      const SizedBox(width: 12),
                      Expanded(child: Text(r.card.name, style: const TextStyle(fontWeight: FontWeight.w500))),
                      TextButton(onPressed: () => state.cancelRequest(r), child: const Text('Cancel')),
                    ]),
                  ),
              ],
              if (friends.isNotEmpty) const SectionLabel('Friends'),
              if (friends.isEmpty && state.incoming.isEmpty && state.outgoing.isEmpty)
                Column(children: [
                  const EmptyState(Icons.chat_bubble_outline_rounded, 'No friends yet',
                      'Add your laptop: in Holm, open Friends › Add friend and copy your code, or give Holm your code from here.'),
                  Row(mainAxisAlignment: MainAxisAlignment.center, children: [
                    PillButton('Add friend', icon: Icons.person_add_alt_rounded, onPressed: () => showAddFriend(context, state)),
                    const SizedBox(width: 8),
                    PillButton('My code', icon: Icons.qr_code_rounded, primary: false, onPressed: () => showMyCode(context, state)),
                  ]),
                ]),
              for (final f in friends) _FriendTile(state: state, friend: f),
            ],
          );
        },
      );
}

class _FriendTile extends StatelessWidget {
  final AppState state;
  final Friend friend;
  const _FriendTile({required this.state, required this.friend});

  @override
  Widget build(BuildContext context) {
    final last = state.chatWith(friend).lastOrNull;
    final unread = state.unread[friend.key] ?? 0;
    final preview = last == null ? 'Say hello' : (last.mine ? 'You: ${last.text}' : last.text);
    return HolmCard(
      padding: const EdgeInsets.all(12),
      color: unread > 0 ? HolmColors.cardStrong : HolmColors.card,
      onTap: () => Navigator.of(context).push(MaterialPageRoute<void>(builder: (_) => ConversationPage(state, friend))),
      child: Row(children: [
        Avatar(friend.name, size: 54, online: state.isOnline(friend)),
        const SizedBox(width: 14),
        Expanded(
          child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
            Row(children: [
              Expanded(child: Text(friend.name, style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600))),
              if (last != null)
                Text(clock(DateTime.fromMillisecondsSinceEpoch(last.sent * 1000)),
                    style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
            ]),
            const SizedBox(height: 4),
            Row(children: [
              Expanded(
                child: Text(preview.replaceAll('\n', ' '),
                    maxLines: 1, overflow: TextOverflow.ellipsis, style: const TextStyle(color: HolmColors.dim)),
              ),
              if (unread > 0)
                Container(
                  padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                  decoration: BoxDecoration(color: HolmColors.accent, borderRadius: BorderRadius.circular(10)),
                  child: Text('$unread', style: const TextStyle(fontSize: 12, fontWeight: FontWeight.w700)),
                ),
            ]),
          ]),
        ),
      ]),
    );
  }
}

Future<void> showAddFriend(BuildContext context, AppState state) async {
  final controller = TextEditingController();
  final clip = (await Clipboard.getData(Clipboard.kTextPlain))?.text;
  if (clip != null && (parseFriendCode(clip) != null || Invite.find(clip) != null)) controller.text = clip.trim();
  if (!context.mounted) return;
  await showModalBottomSheet<void>(
    context: context,
    isScrollControlled: true,
    builder: (context) {
      var busy = false;
      String? message;
      return StatefulBuilder(builder: (context, setSheet) {
        Future<void> add() async {
          setSheet(() => busy = true);
          final result = await state.addFriend(controller.text);
          setSheet(() {
            busy = false;
            message = result;
          });
          if (result.startsWith('Request sent') && context.mounted) {
            Navigator.pop(context);
            ScaffoldMessenger.of(context).showSnackBar(SnackBar(content: Text(result)));
          }
        }

        return Padding(
          padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.of(context).viewInsets.bottom + 24),
          child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
            const Text('Add friend', style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
            const SizedBox(height: 6),
            const Text('Paste an invite code (HOLM-XXXXX-XXXXX) or a long friend code from Holm on your laptop.',
                style: TextStyle(color: HolmColors.dim)),
            const SizedBox(height: 16),
            TextField(
              controller: controller,
              minLines: 1,
              maxLines: 4,
              decoration: const InputDecoration(hintText: 'HOLM-7KQ2X-9XF4M'),
            ),
            if (message != null) ...[
              const SizedBox(height: 10),
              Text(message!, style: const TextStyle(color: HolmColors.gold)),
            ],
            const SizedBox(height: 16),
            Row(children: [
              PillButton(busy ? 'Sending…' : 'Send request', icon: Icons.send_rounded, onPressed: busy ? null : add),
              const SizedBox(width: 8),
              PillButton('My code', primary: false, onPressed: () => showMyCode(context, state)),
            ]),
          ]),
        );
      });
    },
  );
}

Future<void> showMyCode(BuildContext context, AppState state) => showModalBottomSheet<void>(
      context: context,
      builder: (context) => Padding(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 28),
        child: Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
          const Text('Your friend code', style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          const Text('In Holm on your laptop: Friends › Add friend, and paste this code. You see the request here.',
              style: TextStyle(color: HolmColors.dim)),
          const SizedBox(height: 16),
          Container(
            padding: const EdgeInsets.all(14),
            decoration: BoxDecoration(color: HolmColors.card, borderRadius: BorderRadius.circular(14)),
            child: SelectableText(state.me.friendCode,
                maxLines: 4, style: const TextStyle(fontFamily: 'monospace', fontSize: 12.5, color: HolmColors.dim)),
          ),
          const SizedBox(height: 16),
          Row(children: [
            PillButton('Copy', icon: Icons.copy_rounded, onPressed: () {
              Clipboard.setData(ClipboardData(text: state.me.friendCode));
              Navigator.pop(context);
              ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Friend code copied')));
            }),
            const SizedBox(width: 8),
            PillButton('Share', icon: Icons.ios_share_rounded, primary: false,
                onPressed: () => SharePlus.instance.share(ShareParams(text: state.me.friendCode))),
          ]),
        ]),
      ),
    );

class ConversationPage extends StatefulWidget {
  final AppState state;
  final Friend friend;
  const ConversationPage(this.state, this.friend, {super.key});

  @override
  State<ConversationPage> createState() => _ConversationPageState();
}

class _ConversationPageState extends State<ConversationPage> {
  final _input = TextEditingController();
  final _scroll = ScrollController();

  AppState get s => widget.state;
  Friend get f => widget.friend;

  @override
  void initState() {
    super.initState();
    s.markRead(f);
    s.askIfHere(f);
    s.addListener(_onChange);
  }

  @override
  void dispose() {
    s.removeListener(_onChange);
    _input.dispose();
    _scroll.dispose();
    super.dispose();
  }

  void _onChange() {
    if ((s.unread[f.key] ?? 0) > 0) s.markRead(f);
  }

  void _send() {
    final text = _input.text.trim();
    if (text.isEmpty) return;
    _input.clear();
    s.sendChat(f, text);
  }

  void _reactions(ChatMessage m) {
    showModalBottomSheet<void>(
      context: context,
      builder: (context) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 28),
        child: Column(mainAxisSize: MainAxisSize.min, children: [
          Row(mainAxisAlignment: MainAxisAlignment.spaceEvenly, children: [
            for (final e in quickReactions)
              InkWell(
                borderRadius: BorderRadius.circular(24),
                onTap: () {
                  Navigator.pop(context);
                  s.react(f, m, e);
                },
                child: Container(
                  padding: const EdgeInsets.all(8),
                  decoration: BoxDecoration(
                      shape: BoxShape.circle, color: m.myReaction == e ? HolmColors.selected : Colors.transparent),
                  child: Text(e, style: const TextStyle(fontSize: 30)),
                ),
              ),
          ]),
          const SizedBox(height: 12),
          ListTile(
            leading: const Icon(Icons.copy_rounded),
            title: const Text('Copy'),
            onTap: () {
              Navigator.pop(context);
              s.copy(m.text);
            },
          ),
          if (m.state == ChatState.failed)
            ListTile(
              leading: const Icon(Icons.refresh_rounded),
              title: const Text('Send again'),
              onTap: () {
                Navigator.pop(context);
                s.retry(f, m);
              },
            ),
        ]),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
        listenable: s,
        builder: (context, _) {
          final messages = s.chatWith(f).reversed.toList();
          final online = s.isOnline(f);
          return Scaffold(
            backgroundColor: HolmColors.panel,
            appBar: AppBar(
              backgroundColor: HolmColors.panel,
              titleSpacing: 0,
              title: Row(children: [
                Avatar(f.name, size: 40, online: online),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
                    Text(f.name, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
                    Text(online ? 'Active now' : 'Holm',
                        style: TextStyle(fontSize: 13, color: online ? HolmColors.green : HolmColors.dim)),
                  ]),
                ),
              ]),
              actions: [
                IconButton(onPressed: () => s.wave(f), icon: const Text('👋', style: TextStyle(fontSize: 22))),
                PopupMenuButton<String>(
                  color: HolmColors.raised,
                  onSelected: (v) async {
                    if (v == 'remove') {
                      await s.removeFriend(f);
                      if (context.mounted) Navigator.pop(context);
                    }
                  },
                  itemBuilder: (_) => const [PopupMenuItem(value: 'remove', child: Text('Remove friend'))],
                ),
              ],
            ),
            body: Column(children: [
              Expanded(
                child: ListView.builder(
                  controller: _scroll,
                  reverse: true,
                  padding: const EdgeInsets.fromLTRB(12, 12, 12, 12),
                  itemCount: messages.length + 1,
                  itemBuilder: (context, i) {
                    if (i == messages.length) return _ChatIntro(f.name);
                    final m = messages[i];
                    final older = i + 1 < messages.length ? messages[i + 1] : null;
                    final showTime = older == null || m.sent - older.sent > 15 * 60;
                    final isLastMine = i == 0 && m.mine;
                    return Column(children: [
                      if (showTime) _TimeLabel(DateTime.fromMillisecondsSinceEpoch(m.sent * 1000)),
                      _Bubble(
                        message: m,
                        name: f.name,
                        showFace: !m.mine && (i == 0 || messages[i - 1].mine),
                        showState: isLastMine || m.state == ChatState.failed,
                        onLongPress: () => _reactions(m),
                        onDoubleTap: () => s.react(f, m, '❤️'),
                      ),
                    ]);
                  },
                ),
              ),
              SafeArea(
                top: false,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(12, 4, 12, 10),
                  child: Container(
                    decoration: BoxDecoration(color: HolmColors.card, borderRadius: BorderRadius.circular(28)),
                    padding: const EdgeInsets.fromLTRB(18, 4, 6, 4),
                    child: Row(children: [
                      Expanded(
                        child: TextField(
                          controller: _input,
                          minLines: 1,
                          maxLines: 5,
                          textCapitalization: TextCapitalization.sentences,
                          decoration: const InputDecoration(
                            hintText: 'Message…',
                            filled: false,
                            border: InputBorder.none,
                            contentPadding: EdgeInsets.symmetric(vertical: 12),
                          ),
                          onSubmitted: (_) => _send(),
                        ),
                      ),
                      ValueListenableBuilder(
                        valueListenable: _input,
                        builder: (context, value, _) => AnimatedOpacity(
                          duration: const Duration(milliseconds: 150),
                          opacity: value.text.trim().isEmpty ? 0.45 : 1,
                          child: Material(
                            color: HolmColors.accent,
                            shape: const CircleBorder(),
                            child: InkWell(
                              customBorder: const CircleBorder(),
                              onTap: _send,
                              child: const SizedBox(
                                  width: 42, height: 42, child: Icon(Icons.send_rounded, size: 20, color: Colors.white)),
                            ),
                          ),
                        ),
                      ),
                    ]),
                  ),
                ),
              ),
            ]),
          );
        },
      );
}

class _ChatIntro extends StatelessWidget {
  final String name;
  const _ChatIntro(this.name);

  @override
  Widget build(BuildContext context) => Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
        child: Column(children: [
          Text(name, style: const TextStyle(fontSize: 18, fontWeight: FontWeight.w600)),
          const SizedBox(height: 4),
          const Text('Messages are sealed so only the two of you can read them.',
              textAlign: TextAlign.center, style: TextStyle(color: HolmColors.dim, fontSize: 13)),
        ]),
      );
}

class _TimeLabel extends StatelessWidget {
  final DateTime time;
  const _TimeLabel(this.time);

  @override
  Widget build(BuildContext context) {
    final now = DateTime.now();
    final today = time.year == now.year && time.month == now.month && time.day == now.day;
    final day = today ? 'Today' : '${time.day}.${time.month}.${time.year}';
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Text('$day ${clock(time)}', style: const TextStyle(color: HolmColors.dim, fontSize: 13, fontWeight: FontWeight.w500)),
    );
  }
}

bool _onlyEmoji(String text) {
  final t = text.trim();
  if (t.isEmpty || t.characters.length > 3) return false;
  return !RegExp(r'[A-Za-z0-9À-ɏ]').hasMatch(t);
}

class _Bubble extends StatelessWidget {
  final ChatMessage message;
  final String name;
  final bool showFace;
  final bool showState;
  final VoidCallback onLongPress;
  final VoidCallback onDoubleTap;
  const _Bubble({
    required this.message,
    required this.name,
    required this.showFace,
    required this.showState,
    required this.onLongPress,
    required this.onDoubleTap,
  });

  @override
  Widget build(BuildContext context) {
    final m = message;
    final big = _onlyEmoji(m.text);
    final reaction = m.mine ? m.theirReaction : m.myReaction;
    final otherReaction = m.mine ? m.myReaction : m.theirReaction;
    final reactions = [reaction, otherReaction].whereType<String>().toSet().join(' ');

    final bubble = GestureDetector(
      onLongPress: onLongPress,
      onDoubleTap: onDoubleTap,
      child: big
          ? Padding(
              padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 2),
              child: Text(m.text, style: const TextStyle(fontSize: 44)),
            )
          : Container(
              constraints: BoxConstraints(maxWidth: MediaQuery.of(context).size.width * 0.72),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 11),
              decoration: BoxDecoration(
                gradient: m.mine ? HolmColors.sentGradient : null,
                color: m.mine ? null : HolmColors.raised,
                borderRadius: BorderRadius.circular(22),
              ),
              child: Text(m.text, style: const TextStyle(fontSize: 16, height: 1.3, color: Colors.white)),
            ),
    );

    return Padding(
      padding: EdgeInsets.only(top: 3, bottom: reactions.isEmpty ? 3 : 14),
      child: Column(crossAxisAlignment: m.mine ? CrossAxisAlignment.end : CrossAxisAlignment.start, children: [
        Row(
          mainAxisAlignment: m.mine ? MainAxisAlignment.end : MainAxisAlignment.start,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            if (!m.mine) SizedBox(width: 38, child: showFace ? Avatar(name, size: 30) : null),
            Stack(clipBehavior: Clip.none, children: [
              bubble,
              if (reactions.isNotEmpty)
                Positioned(
                  bottom: -14,
                  right: m.mine ? 10 : null,
                  left: m.mine ? null : 10,
                  child: Container(
                    padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
                    decoration: BoxDecoration(
                      color: HolmColors.raised,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: HolmColors.panel, width: 2),
                    ),
                    child: Text(reactions, style: const TextStyle(fontSize: 14)),
                  ),
                ),
            ]),
          ],
        ),
        if (m.mine && showState)
          Padding(
            padding: const EdgeInsets.only(top: 4, right: 6),
            child: Text(
              switch (m.state) {
                ChatState.sending => 'Sending…',
                ChatState.sent => 'Sent',
                ChatState.failed => 'Not sent · hold to try again',
                ChatState.received => '',
              },
              style: TextStyle(fontSize: 12, color: m.state == ChatState.failed ? HolmColors.red : HolmColors.dim),
            ),
          ),
      ]),
    );
  }
}
