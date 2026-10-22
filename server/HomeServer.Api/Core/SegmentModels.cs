namespace HomeServer.Core;
public sealed record MediaSegment(string Kind,double StartSeconds,double EndSeconds,string Source="manual",double Confidence=1);
public sealed record MediaSegmentSet(string MediaId,int Revision,string Fingerprint,int MatchRevision,MediaSegment[] Segments);
public sealed record SegmentInput(int ExpectedRevision,int ExpectedMatchRevision,string Fingerprint,MediaSegment[] Segments);
public sealed record SegmentAnalysisInput(bool SharedAudio=true,bool Recap=true,bool Credits=true,bool Chapters=true,bool AutoApply=false);
public sealed record SegmentAnalysisPlan(SegmentAnalysisInput Options,string Fingerprint,int MatchRevision,int SegmentRevision);
public sealed record SegmentProposal(MediaSegment[] Segments,string[] Evidence,string[] Warnings,string Fingerprint,int MatchRevision,int SegmentRevision);
public sealed record PlayerPreferences(bool AutoSkipIntro=false,bool AutoSkipRecap=false,bool AutoSkipCredits=false,bool AutoSkipCommercials=false);
