import 'package:material_ui/material_ui.dart';

import '../app_state.dart';
import 'theme.dart';
import 'widgets.dart';

/// The Holm account: sign in with the same email and password as in Holm on the PC (Settings › Account), or sign out.
Future<void> showAccount(BuildContext context, AppState state) => showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      builder: (context) => Padding(
        padding: EdgeInsets.fromLTRB(20, 0, 20, MediaQuery.of(context).viewInsets.bottom + 28),
        child: ListenableBuilder(
          listenable: state,
          builder: (context, _) => state.account == null ? _SignIn(state) : _SignedIn(state),
        ),
      ),
    );

class _SignIn extends StatefulWidget {
  final AppState state;
  const _SignIn(this.state);

  @override
  State<_SignIn> createState() => _SignInState();
}

class _SignInState extends State<_SignIn> {
  final _email = TextEditingController(), _password = TextEditingController();
  String? _error;
  bool _hide = true;

  Future<void> _go() async {
    if (_email.text.trim().isEmpty || _password.text.isEmpty) {
      setState(() => _error = 'Type the email and password of your Holm account.');
      return;
    }
    setState(() => _error = null);
    final error = await widget.state.signIn(_email.text, _password.text);
    if (!mounted) return;
    if (error != null) {
      setState(() => _error = error);
    } else {
      _password.clear();
    }
  }

  @override
  void dispose() {
    _email.dispose();
    _password.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final busy = widget.state.accountBusy;
    return Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
      const Text('Holm account', style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
      const SizedBox(height: 6),
      const Text(
          'Sign in as in Holm on your PC (Settings › Account). Your PCs then reach this phone on any network, and your friends come along.',
          style: TextStyle(color: HolmColors.dim, height: 1.35)),
      const SizedBox(height: 18),
      TextField(
        controller: _email,
        enabled: !busy,
        keyboardType: TextInputType.emailAddress,
        autofillHints: const [AutofillHints.email],
        autocorrect: false,
        decoration: const InputDecoration(hintText: 'Email'),
      ),
      const SizedBox(height: 10),
      TextField(
        controller: _password,
        enabled: !busy,
        obscureText: _hide,
        autofillHints: const [AutofillHints.password],
        onSubmitted: (_) => _go(),
        decoration: InputDecoration(
          hintText: 'Password',
          suffixIcon: IconButton(
            icon: Icon(_hide ? Icons.visibility_outlined : Icons.visibility_off_outlined, color: HolmColors.dim),
            onPressed: () => setState(() => _hide = !_hide),
          ),
        ),
      ),
      if (_error != null) ...[
        const SizedBox(height: 10),
        Text(_error!, style: const TextStyle(color: HolmColors.red, fontSize: 13.5)),
      ],
      const SizedBox(height: 16),
      Row(children: [
        PillButton(busy ? 'Signing in…' : 'Sign in', icon: Icons.login_rounded, onPressed: busy ? null : _go),
        if (busy) ...[
          const SizedBox(width: 14),
          const SizedBox(width: 18, height: 18, child: CircularProgressIndicator(strokeWidth: 2, color: HolmColors.accent)),
        ],
      ]),
      const SizedBox(height: 14),
      const Text('Your password stays on this phone: Holm only sends a key made from it, and your data is encrypted.',
          style: TextStyle(color: HolmColors.faint, fontSize: 12.5)),
    ]);
  }
}

class _SignedIn extends StatelessWidget {
  final AppState state;
  const _SignedIn(this.state);

  Future<void> _signOut(BuildContext context) async {
    final sure = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Sign out?'),
        content: const Text(
            'Your friends and chats from the account leave this phone, and your PCs no longer reach it. They stay in the account.',
            style: TextStyle(color: HolmColors.dim)),
        actions: [
          TextButton(onPressed: () => Navigator.pop(context, false), child: const Text('Cancel')),
          FilledButton(onPressed: () => Navigator.pop(context, true), child: const Text('Sign out')),
        ],
      ),
    );
    if (sure == true) state.signOut();
  }

  @override
  Widget build(BuildContext context) {
    final devices = state.ownDevices;
    return Column(mainAxisSize: MainAxisSize.min, crossAxisAlignment: CrossAxisAlignment.start, children: [
      const Text('Holm account', style: TextStyle(fontSize: 24, fontWeight: FontWeight.w600)),
      const SizedBox(height: 14),
      HolmCard(
        margin: EdgeInsets.zero,
        child: Row(children: [
          Avatar(state.me.name, size: 44),
          const SizedBox(width: 14),
          Expanded(
            child: Column(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Text(state.me.name, style: const TextStyle(fontSize: 16, fontWeight: FontWeight.w600)),
              Text(state.account!.email, style: const TextStyle(color: HolmColors.dim, fontSize: 13)),
            ]),
          ),
        ]),
      ),
      const SizedBox(height: 14),
      Text(
        state.accountError ??
            (devices.isEmpty
                ? 'No other device of yours is online right now.'
                : 'Online now: ${devices.map((d) => d.name).join(', ')}'),
        style: TextStyle(color: state.accountError != null ? HolmColors.red : HolmColors.dim),
      ),
      const SizedBox(height: 16),
      Row(children: [
        PillButton('Update', icon: Icons.sync_rounded, primary: false, onPressed: () {
          state.refreshAccount();
          state.announceDevice(force: true);
        }),
        const SizedBox(width: 8),
        PillButton('Sign out', icon: Icons.logout_rounded, primary: false, onPressed: () => _signOut(context)),
      ]),
    ]);
  }
}
